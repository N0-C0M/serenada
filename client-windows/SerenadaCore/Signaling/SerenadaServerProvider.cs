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
    private JoinOptions? _lastJoinOptions;
    private CancellationTokenSource? _reconnectTokenRefreshCts;
    private CancellationTokenSource? _turnRefreshCts;

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
            return;

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
        CancelReconnectTokenRefresh();
        CancelTurnRefresh();
        _engine?.Disconnect();
    }

    public override void JoinRoom(string roomId, JoinOptions options)
    {
        if (_roomId != null && _roomId != roomId)
        {
            _ourCid = null;
            _reconnectToken = null;
        }
        _roomId = roomId;
        _lastJoinOptions = options;
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
        if (_roomId == null)
            return;

        var msg = SignalingMessage.Outbound(
            SignalingProtocolConstants.TypeLeave,
            rid: _roomId,
            cid: _ourCid);
        _engine?.Send(msg);
        _roomId = null;
        _ourCid = null;
        _reconnectToken = null;
        _lastJoinOptions = null;
        CancelReconnectTokenRefresh();
        CancelTurnRefresh();
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
                RaiseJoined(joined with { LocalCid = _ourCid });
                if (joined.TurnTokenTtlMs is { } turnTtlMs)
                    ScheduleTurnRefresh(turnTtlMs);

                // Schedule reconnect token refresh
                if (joined.ReconnectTokenTtlMs is { } ttl)
                {
                    var leeway = WebRtcResilienceConstants.ReconnectTokenRefreshLeewayMs;
                    var delay = ttl > leeway
                        ? ttl - leeway
                        : Math.Max(30_000, ttl / 2);
                    ScheduleReconnectTokenRefresh(delay);
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
                    // Auto-retry fresh join on invalid reconnect token
                    if (error.Code == SignalingProtocolConstants.ErrorInvalidReconnectToken &&
                        _roomId != null &&
                        _lastJoinOptions != null)
                    {
                        Log(SerenadaLogLevel.Warning, "Provider", "Reconnect token invalid — retrying fresh join.");
                        _ourCid = null;
                        _reconnectToken = null;
                        var freshOptions = _lastJoinOptions with
                        {
                            ReconnectCid = null,
                            ReconnectToken = null,
                        };
                        JoinRoom(_roomId, freshOptions);
                        break;
                    }

                    RaiseError(error);
                }
                break;

            case SignalingProtocolConstants.TypeTurnRefreshed:
                var turn = SignalingPayloadParsers.ParseTurnRefreshed(payload);
                if (turn != null)
                {
                    _turnToken = turn.TurnToken;
                    _turnTokenExpiresAt = turn.TurnTokenExpiresAt;
                    if (turn.TurnTokenTtlMs is { } refreshedTurnTtlMs)
                        ScheduleTurnRefresh(refreshedTurnTtlMs);
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
                    var refreshedReconnectTtl = rt.ReconnectTokenTtlMs
                        ?? WebRtcResilienceConstants.ReconnectTokenTtlFallbackMs;
                    var leeway = WebRtcResilienceConstants.ReconnectTokenRefreshLeewayMs;
                    var delay = refreshedReconnectTtl > leeway
                        ? refreshedReconnectTtl - leeway
                        : Math.Max(30_000, refreshedReconnectTtl / 2);
                    ScheduleReconnectTokenRefresh(delay);
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
        var reconnectCid = _ourCid ?? options.ReconnectCid;
        var reconnectToken = _reconnectToken ?? options.ReconnectToken;
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
            ["reconnectCid"] = reconnectCid,
            ["reconnectToken"] = reconnectToken,
        };
    }

    private void EmitParticipantDiffs(IReadOnlyList<SignalingParticipant> currentParticipants)
    {
        // PeerJoined/PeerLeft events are handled by the session via room_state.
        // For now, the session reconstructs the full list from each room_state.
        // Future optimization: track previous set and emit diffs.
    }

    private void ScheduleReconnectTokenRefresh(int delayMs)
    {
        CancelReconnectTokenRefresh();
        _reconnectTokenRefreshCts = new CancellationTokenSource();
        _ = ScheduleReconnectTokenRefreshAsync(
            delayMs,
            _reconnectTokenRefreshCts.Token);
    }

    private async Task ScheduleReconnectTokenRefreshAsync(
        int delayMs,
        CancellationToken ct)
    {
        try
        {
            await Task.Delay(delayMs, ct);
            if (ct.IsCancellationRequested || _roomId == null)
                return;
            var msg = SignalingMessage.Outbound(
                SignalingProtocolConstants.TypeReconnectTokenRefresh,
                rid: _roomId,
                cid: _ourCid);
            _engine?.Send(msg);
        }
        catch (OperationCanceledException)
        {
            // Expected when leaving or refreshing the schedule.
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

    private void ScheduleTurnRefresh(int ttlMs)
    {
        CancelTurnRefresh();
        var delayMs = Math.Max(
            30_000,
            (int)(ttlMs * WebRtcResilienceConstants.TurnRefreshTriggerRatio));
        _turnRefreshCts = new CancellationTokenSource();
        _ = SendTurnRefreshAfterDelayAsync(delayMs, _turnRefreshCts.Token);
    }

    private async Task SendTurnRefreshAfterDelayAsync(
        int delayMs,
        CancellationToken ct)
    {
        try
        {
            await Task.Delay(delayMs, ct);
            if (ct.IsCancellationRequested || _roomId == null)
                return;

            _engine?.Send(SignalingMessage.Outbound(
                SignalingProtocolConstants.TypeTurnRefresh,
                rid: _roomId,
                cid: _ourCid));
        }
        catch (OperationCanceledException)
        {
            // Expected when the room is left.
        }
    }

    private void Log(SerenadaLogLevel level, string tag, string message)
    {
        _logger?.Log(level, tag, message);
    }

    private void CancelReconnectTokenRefresh()
    {
        _reconnectTokenRefreshCts?.Cancel();
        _reconnectTokenRefreshCts?.Dispose();
        _reconnectTokenRefreshCts = null;
    }

    private void CancelTurnRefresh()
    {
        _turnRefreshCts?.Cancel();
        _turnRefreshCts?.Dispose();
        _turnRefreshCts = null;
    }
}
