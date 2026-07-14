using Serenada.Core.Models;

namespace Serenada.Core.Signaling;

/// <summary>
/// Routes inbound signaling messages to the appropriate session handlers.
/// Dispatches by message type and decodes typed payloads.
/// Mirrors the cross-platform <c>SignalingMessageRouter</c> on Android and iOS.
/// </summary>
internal class SignalingMessageRouter
{
    private readonly Func<string> _getRoomId;
    private readonly Action<JoinedPayload> _onJoined;
    private readonly Action<RoomStatePayload> _onRoomState;
    private readonly Action<string> _onRoomEnded;
    private readonly Action<ErrorPayload> _onError;
    private readonly Action _onPong;
    private readonly Action<TurnRefreshedPayload> _onTurnRefreshed;
    private readonly Action<PeerMessage> _onSignalingPayload;
    private readonly Action<ContentStatePayload> _onContentState;
    private readonly Action<NegotiationDirtyPayload> _onNegotiationDirty;
    private readonly Action<RelayFailedPayload> _onRelayFailed;
    private readonly Action<ReconnectTokenRefreshedPayload> _onReconnectTokenRefreshed;
    private readonly Action<SerenadaLogLevel, string, string> _log;

    public SignalingMessageRouter(
        Func<string> getRoomId,
        Action<JoinedPayload> onJoined,
        Action<RoomStatePayload> onRoomState,
        Action<string> onRoomEnded,
        Action<ErrorPayload> onError,
        Action onPong,
        Action<TurnRefreshedPayload> onTurnRefreshed,
        Action<PeerMessage> onSignalingPayload,
        Action<ContentStatePayload> onContentState,
        Action<NegotiationDirtyPayload> onNegotiationDirty,
        Action<RelayFailedPayload> onRelayFailed,
        Action<ReconnectTokenRefreshedPayload> onReconnectTokenRefreshed,
        Action<SerenadaLogLevel, string, string> log)
    {
        _getRoomId = getRoomId;
        _onJoined = onJoined;
        _onRoomState = onRoomState;
        _onRoomEnded = onRoomEnded;
        _onError = onError;
        _onPong = onPong;
        _onTurnRefreshed = onTurnRefreshed;
        _onSignalingPayload = onSignalingPayload;
        _onContentState = onContentState;
        _onNegotiationDirty = onNegotiationDirty;
        _onRelayFailed = onRelayFailed;
        _onReconnectTokenRefreshed = onReconnectTokenRefreshed;
        _log = log;
    }

    // ── Dispatch from raw signaling messages ──────────────────

    public void ProcessMessage(SignalingMessage msg)
    {
        var payload = msg.Payload;

        switch (msg.Type)
        {
            case SignalingProtocolConstants.TypeJoined:
                var joined = SignalingPayloadParsers.ParseJoined(payload);
                if (joined != null) _onJoined(joined);
                break;

            case SignalingProtocolConstants.TypeRoomState:
                var roomState = SignalingPayloadParsers.ParseRoomState(payload);
                if (roomState != null) _onRoomState(roomState);
                break;

            case SignalingProtocolConstants.TypeRoomEnded:
                _onRoomEnded(payload?.GetProperty("by").GetString() ?? "unknown");
                break;

            case SignalingProtocolConstants.TypePong:
                _onPong();
                break;

            case SignalingProtocolConstants.TypeTurnRefreshed:
                var turn = SignalingPayloadParsers.ParseTurnRefreshed(payload);
                if (turn != null) _onTurnRefreshed(turn);
                break;

            case SignalingProtocolConstants.TypeReconnectTokenRefreshed:
                var rt = SignalingPayloadParsers.ParseReconnectTokenRefreshed(payload);
                if (rt != null) _onReconnectTokenRefreshed(rt);
                break;

            case SignalingProtocolConstants.TypeNegotiationDirty:
                var nd = SignalingPayloadParsers.ParseNegotiationDirty(payload);
                if (nd != null) _onNegotiationDirty(nd);
                break;

            case SignalingProtocolConstants.TypeRelayFailed:
                var rf = SignalingPayloadParsers.ParseRelayFailed(payload);
                if (rf != null) _onRelayFailed(rf);
                break;

            case SignalingProtocolConstants.TypeError:
                var error = SignalingPayloadParsers.ParseError(payload);
                if (error != null) _onError(error);
                break;

            // Peer-relayed messages (offer, answer, ice, etc.)
            case SignalingProtocolConstants.TypeOffer:
            case SignalingProtocolConstants.TypeAnswer:
            case SignalingProtocolConstants.TypeIce:
            case SignalingProtocolConstants.TypeMediaRestartRequest:
            case SignalingProtocolConstants.TypeContentState:
            case SignalingProtocolConstants.TypeParticipantMediaState:
                ProcessPeerMessage(msg);
                break;

            default:
                _log(SerenadaLogLevel.Debug, "Router", $"Unknown message type: {msg.Type}");
                break;
        }
    }

    // ── Process provider events (already decoded) ─────────────

    public void ProcessJoined(JoinedPayload payload) => _onJoined(payload);
    public void ProcessRoomState(RoomStatePayload payload) => _onRoomState(payload);
    public void ProcessRoomEnded(string by) => _onRoomEnded(by);
    public void ProcessError(ErrorPayload error) => _onError(error);
    public void ProcessNegotiationDirty(NegotiationDirtyPayload p) => _onNegotiationDirty(p);
    public void ProcessRelayFailed(RelayFailedPayload p) => _onRelayFailed(p);
    public void ProcessReconnectTokenRefreshed(ReconnectTokenRefreshedPayload p) => _onReconnectTokenRefreshed(p);

    public void ProcessPeerJoined(SignalingParticipant participant) { /* Handled via room_state */ }
    public void ProcessPeerLeft(SignalingParticipant participant) { /* Handled via room_state */ }

    public void ProcessPeerMessage(SignalingMessage msg)
    {
        var from = msg.Cid ?? string.Empty;
        var payload = msg.Payload;

        if (msg.Type == SignalingProtocolConstants.TypeContentState)
        {
            var cs = SignalingPayloadParsers.ParseContentState(payload, msg.Sid);
            if (cs != null) _onContentState(cs);
            return;
        }

        // Forward as a peer message for the media engine to handle
        _onSignalingPayload(new PeerMessage
        {
            Type = msg.Type,
            From = from,
            Sid = msg.Sid,
            Payload = payload,
        });
    }

    public void ProcessPeerMessage(PeerMessage msg)
    {
        // Check for content_state relayed as a peer message
        if (msg.Type == SignalingProtocolConstants.TypeContentState &&
            msg.Payload is { } p)
        {
            var cs = SignalingPayloadParsers.ParseContentState(p, msg.Sid);
            if (cs != null) _onContentState(cs);
            return;
        }

        _onSignalingPayload(msg);
    }

    // ── Outbound helpers ─────────────────────────────────────

    // (Outbound broadcast helpers will be wired when the media engine is integrated)
}
