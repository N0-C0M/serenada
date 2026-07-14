using System.Text.Json;
using Serenada.Core.Models;
using Serenada.Core.Networking;

namespace Serenada.Core.Signaling;

/// <summary>
/// Built-in signaling provider that connects to the Serenada server via WebSocket
/// (primary) and SSE (fallback). Wraps a <see cref="SignalingEngine"/> for dual-transport
/// management.
///
/// Mirrors <c>SerenadaServerProvider</c> on Android and iOS.
/// </summary>
internal class SerenadaServerProvider : SignalingProviderBase
{
    private readonly string _serverHost;
    private readonly IReadOnlyList<SerenadaTransport> _transports;
    private readonly ISerenadaLogger? _logger;
    private readonly CoreApiClient _apiClient;

    private SignalingEngine? _engine;
    private string? _turnToken;
    private long? _turnTokenExpiresAt;
    private string? _reconnectToken;
    private string? _roomId;
    private string? _ourCid;
    private int _joinAttempt;

    public override int Version => SupportedVersion;

    public override ProviderCapabilities Capabilities { get; } = new()
    {
        HandlesReconnection = true,
    };

    public SerenadaServerProvider(
        string serverHost,
        IReadOnlyList<SerenadaTransport> transports,
        ISerenadaLogger? logger)
    {
        _serverHost = serverHost;
        _transports = transports;
        _logger = logger;
        _apiClient = new CoreApiClient();
    }

    // ── ISignalingProvider implementation ─────────────────────

    public override void Connect()
    {
        if (_engine != null)
        {
            _engine.Dispose();
        }

        _engine = new SignalingEngine(
            serverHost: _serverHost,
            transports: _transports,
            logger: _logger);

        _engine.OnOpen += HandleTransportOpen;
        _engine.OnMessage += HandleTransportMessage;
        _engine.OnClosed += HandleTransportClosed;

        _ = _engine.ConnectAsync();
    }

    public override void Disconnect()
    {
        _engine?.Disconnect();
    }

    public override void JoinRoom(string roomId, JoinOptions options)
    {
        _roomId = roomId;
        _joinAttempt++;

        var payload = BuildJoinPayload(options);
        var msg = SignalingMessage.Outbound(
            SignalingProtocolConstants.TypeJoin,
            rid: roomId,
            payload: payload);

        _engine?.Send(msg);

        Log(SerenadaLogLevel.Info, "Provider", $"Join sent (attempt {_joinAttempt}) for room {roomId}.");
    }

    public override void LeaveRoom()
    {
        var msg = SignalingMessage.Outbound(
            SignalingProtocolConstants.TypeLeave,
            rid: _roomId,
            cid: _ourCid);
        _engine?.Send(msg);
        _roomId = null;
    }

    public override void EndRoom()
    {
        var msg = SignalingMessage.Outbound(
            SignalingProtocolConstants.TypeEndRoom,
            rid: _roomId,
            cid: _ourCid,
            payload: new { reason = "host_ended" });
        _engine?.Send(msg);
    }

    public override void SendToPeer(string peerId, string type, object? payload)
    {
        var msg = SignalingMessage.Outbound(type,
            rid: _roomId,
            cid: _ourCid,
            to: peerId,
            payload: payload);
        _engine?.Send(msg);
    }

    public override void Broadcast(string type, object? payload)
    {
        var msg = SignalingMessage.Outbound(type,
            rid: _roomId,
            cid: _ourCid,
            payload: payload);
        _engine?.Send(msg);
    }

    public override async Task<IReadOnlyList<IceServer>> GetIceServersAsync()
    {
        if (_turnToken == null)
            return [];

        try
        {
            using var fetchCts = new CancellationTokenSource(
                TimeSpan.FromMilliseconds(WebRtcResilienceConstants.TurnFetchTimeoutMs));

            var creds = await _apiClient.FetchTurnCredentialsAsync(_serverHost, _turnToken, fetchCts.Token);
            if (creds?.Uris is not { Count: > 0 })
                return [];

            return
            [
                new IceServer
                {
                    Urls = creds.Uris,
                    Username = creds.Username,
                    Password = creds.Password,
                },
            ];
        }
        catch (Exception ex)
        {
            Log(SerenadaLogLevel.Warning, "Provider", $"TURN credential fetch failed: {ex.Message}");
            return [];
        }
    }

    // ── Transport event handlers ──────────────────────────────

    private void HandleTransportOpen(string transportKind)
    {
        Log(SerenadaLogLevel.Info, "Provider", $"Transport opened: {transportKind}.");
        RaiseConnected(new ConnectionInfo { Transport = transportKind });
    }

    private void HandleTransportMessage(SignalingMessage msg)
    {
        var payload = msg.Payload;

        switch (msg.Type)
        {
            case SignalingProtocolConstants.TypeJoined:
                var joined = SignalingPayloadParsers.ParseJoined(payload);
                if (joined == null) break;
                _ourCid = msg.Cid ?? joined.Participants.FirstOrDefault()?.Cid;
                _turnToken = joined.TurnToken;
                _turnTokenExpiresAt = joined.TurnTokenExpiresAt;
                _reconnectToken = joined.ReconnectToken;
                RaiseJoined(joined);

                // Schedule reconnect token refresh
                if (joined.ReconnectTokenTtlMs is { } ttl)
                {
                    var leeway = WebRtcResilienceConstants.ReconnectTokenRefreshLeewayMs;
                    if (ttl > leeway)
                        _ = ScheduleReconnectTokenRefreshAsync(ttl - leeway);
                }
                break;

            case SignalingProtocolConstants.TypeRoomState:
                var roomState = SignalingPayloadParsers.ParseRoomState(payload);
                if (roomState == null) break;

                // Compute diffs for peerJoined/peerLeft
                EmitParticipantDiffs(roomState.Participants);
                RaiseRoomStateUpdated(roomState);
                break;

            case SignalingProtocolConstants.TypeRoomEnded:
                var by = payload?.GetProperty("by").GetString() ?? "host";
                RaiseRoomEnded(by);
                break;

            case SignalingProtocolConstants.TypeError:
                var error = SignalingPayloadParsers.ParseError(payload);
                if (error != null)
                {
                    RaiseError(error);

                    // Auto-retry fresh join on invalid reconnect token
                    if (error.Code == SignalingProtocolConstants.ErrorInvalidReconnectToken)
                    {
                        Log(SerenadaLogLevel.Warning, "Provider", "Reconnect token invalid — retrying fresh join.");
                        if (_roomId != null)
                            JoinRoom(_roomId, new JoinOptions()); // fresh join
                    }
                }
                break;

            case SignalingProtocolConstants.TypeTurnRefreshed:
                var turn = SignalingPayloadParsers.ParseTurnRefreshed(payload);
                if (turn != null)
                {
                    _turnToken = turn.TurnToken;
                    // Fetch new ICE servers
                    _ = RefreshIceServersAsync();
                }
                break;

            case SignalingProtocolConstants.TypeReconnectTokenRefreshed:
                var rt = SignalingPayloadParsers.ParseReconnectTokenRefreshed(payload);
                if (rt != null)
                {
                    _reconnectToken = rt.ReconnectToken;
                    RaiseReconnectTokenRefreshed(rt);
                }
                break;

            case SignalingProtocolConstants.TypeNegotiationDirty:
                var nd = SignalingPayloadParsers.ParseNegotiationDirty(payload);
                if (nd != null) RaiseNegotiationDirty(nd);
                break;

            case SignalingProtocolConstants.TypeRelayFailed:
                var rf = SignalingPayloadParsers.ParseRelayFailed(payload);
                if (rf != null) RaiseRelayFailed(rf);
                break;

            case SignalingProtocolConstants.TypePong:
                // Keepalive — handled by engine's ping/pong mechanism
                break;

            default:
                // Forward peer messages (offer, answer, ice, content_state, etc.)
                RaiseMessage(new PeerMessage
                {
                    Type = msg.Type,
                    From = msg.Cid ?? string.Empty,
                    Sid = msg.Sid,
                    Payload = payload,
                });
                break;
        }
    }

    private void HandleTransportClosed(string reason)
    {
        Log(SerenadaLogLevel.Warning, "Provider", $"Transport closed: {reason}.");
        RaiseDisconnected(reason);
    }

    // ── Helpers ───────────────────────────────────────────────

    private object BuildJoinPayload(JoinOptions options)
    {
        return new Dictionary<string, object?>
        {
            ["device"] = options.Device,
            ["ua"] = options.Ua,
            ["capabilities"] = new Dictionary<string, object?>
            {
                ["trickleIce"] = true,
                ["maxParticipants"] = options.MaxParticipants,
                ["independentContentVideo"] = options.IndependentContentVideo,
            },
            ["mediaPolicy"] = new Dictionary<string, object?>
            {
                ["videoMediaEnabled"] = options.VideoMediaEnabled,
            },
            ["createMaxParticipants"] = options.CreateMaxParticipants,
            ["displayName"] = options.DisplayName,
            ["peerId"] = options.PeerId,
            ["reconnectCid"] = options.ReconnectCid,
        };
    }

    private void EmitParticipantDiffs(IReadOnlyList<SignalingParticipant> currentParticipants)
    {
        // PeerJoined/PeerLeft events are handled by the session via room_state.
        // For now, the session reconstructs the full list from each room_state.
        // Future optimization: track previous set and emit diffs.
    }

    private async Task ScheduleReconnectTokenRefreshAsync(int delayMs)
    {
        try
        {
            await Task.Delay(delayMs);
            var msg = SignalingMessage.Outbound(
                SignalingProtocolConstants.TypeReconnectTokenRefresh,
                rid: _roomId,
                cid: _ourCid);
            _engine?.Send(msg);
        }
        catch (Exception ex)
        {
            Log(SerenadaLogLevel.Warning, "Provider", $"Reconnect token refresh failed: {ex.Message}");
        }
    }

    private async Task RefreshIceServersAsync()
    {
        try
        {
            var servers = await GetIceServersAsync();
            if (servers.Count > 0)
                RaiseIceServersChanged(servers);
        }
        catch (Exception ex)
        {
            Log(SerenadaLogLevel.Warning, "Provider", $"ICE server refresh failed: {ex.Message}");
        }
    }

    private void Log(SerenadaLogLevel level, string tag, string message)
    {
        _logger?.Log(level, tag, message);
    }
}
