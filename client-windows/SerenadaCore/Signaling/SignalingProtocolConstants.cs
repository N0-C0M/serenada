namespace Serenada.Core.Signaling;

/// <summary>
/// Shared signaling protocol wire constants. Verified by
/// <c>scripts/check-signaling-protocol-constants.mjs</c>.
/// </summary>
public static class SignalingProtocolConstants
{
    /// <summary>Current protocol version.</summary>
    public const int ProtocolVersion = 1;

    // ── Message types (client → server) ───────────────────────

    /// <summary><c>"join"</c> — join a room.</summary>
    public const string TypeJoin = "join";

    /// <summary><c>"leave"</c> — leave the room.</summary>
    public const string TypeLeave = "leave";

    /// <summary><c>"end_room"</c> — host ends the call for everyone.</summary>
    public const string TypeEndRoom = "end_room";

    /// <summary><c>"offer"</c> — SDP offer.</summary>
    public const string TypeOffer = "offer";

    /// <summary><c>"answer"</c> — SDP answer.</summary>
    public const string TypeAnswer = "answer";

    /// <summary><c>"ice"</c> — ICE candidate.</summary>
    public const string TypeIce = "ice";

    /// <summary><c>"ping"</c> — keepalive.</summary>
    public const string TypePing = "ping";

    /// <summary><c>"participant_media_state"</c> — audio/video enabled state.</summary>
    public const string TypeParticipantMediaState = "participant_media_state";

    /// <summary><c>"content_state"</c> — screen share presentation state.</summary>
    public const string TypeContentState = "content_state";

    /// <summary><c>"media_restart_request"</c> — ask peer to re-offer.</summary>
    public const string TypeMediaRestartRequest = "media_restart_request";

    /// <summary><c>"media_liveness"</c> — inbound media liveness hint.</summary>
    public const string TypeMediaLiveness = "media_liveness";

    /// <summary><c>"watch_rooms"</c> — subscribe to room occupancy.</summary>
    public const string TypeWatchRooms = "watch_rooms";

    /// <summary><c>"reconnect-token-refresh"</c> — refresh reconnect authority.</summary>
    public const string TypeReconnectTokenRefresh = "reconnect-token-refresh";

    // ── Message types (server → client) ───────────────────────

    /// <summary><c>"joined"</c> — join acknowledged.</summary>
    public const string TypeJoined = "joined";

    /// <summary><c>"room_state"</c> — room membership update.</summary>
    public const string TypeRoomState = "room_state";

    /// <summary><c>"room_ended"</c> — host ended the room.</summary>
    public const string TypeRoomEnded = "room_ended";

    /// <summary><c>"error"</c> — error response.</summary>
    public const string TypeError = "error";

    /// <summary><c>"pong"</c> — ping response.</summary>
    public const string TypePong = "pong";

    /// <summary><c>"turn-refreshed"</c> — new TURN token.</summary>
    public const string TypeTurnRefreshed = "turn-refreshed";

    /// <summary><c>"reconnect-token-refreshed"</c> — new reconnect token.</summary>
    public const string TypeReconnectTokenRefreshed = "reconnect-token-refreshed";

    /// <summary><c>"negotiation_dirty"</c> — peer reattached, needs fresh negotiation.</summary>
    public const string TypeNegotiationDirty = "negotiation_dirty";

    /// <summary><c>"relay_failed"</c> — message could not be delivered to suspended peer.</summary>
    public const string TypeRelayFailed = "relay_failed";

    /// <summary><c>"room_statuses"</c> — initial room status batch.</summary>
    public const string TypeRoomStatuses = "room_statuses";

    /// <summary><c>"room_status_update"</c> — single room occupancy update.</summary>
    public const string TypeRoomStatusUpdate = "room_status_update";

    // ── Error codes ───────────────────────────────────────────

    /// <summary><c>"BAD_REQUEST"</c></summary>
    public const string ErrorBadRequest = "BAD_REQUEST";

    /// <summary><c>"UNSUPPORTED_VERSION"</c></summary>
    public const string ErrorUnsupportedVersion = "UNSUPPORTED_VERSION";

    /// <summary><c>"ROOM_FULL"</c></summary>
    public const string ErrorRoomFull = "ROOM_FULL";

    /// <summary><c>"ROOM_CAPACITY_UNSUPPORTED"</c></summary>
    public const string ErrorRoomCapacityUnsupported = "ROOM_CAPACITY_UNSUPPORTED";

    /// <summary><c>"NOT_HOST"</c></summary>
    public const string ErrorNotHost = "NOT_HOST";

    /// <summary><c>"ROOM_ENDED"</c></summary>
    public const string ErrorRoomEnded = "ROOM_ENDED";

    /// <summary><c>"INVALID_RECONNECT_TOKEN"</c></summary>
    public const string ErrorInvalidReconnectToken = "INVALID_RECONNECT_TOKEN";

    /// <summary><c>"INTERNAL"</c></summary>
    public const string ErrorInternal = "INTERNAL";

    // ── Content types ─────────────────────────────────────────

    /// <summary><c>"screenShare"</c></summary>
    public const string ContentTypeScreenShare = "screenShare";

    // ── Reconnect outcomes ────────────────────────────────────

    /// <summary><c>"fresh"</c> — new participant identity.</summary>
    public const string ReconnectFresh = "fresh";

    /// <summary><c>"reattached"</c> — transport reattached to existing participant.</summary>
    public const string ReconnectReattached = "reattached";

    /// <summary><c>"recovered"</c> — participant identity recovered from token.</summary>
    public const string ReconnectRecovered = "recovered";

    // ── Connection statuses ───────────────────────────────────

    /// <summary><c>"active"</c></summary>
    public const string ConnectionStatusActive = "active";

    /// <summary><c>"suspended"</c></summary>
    public const string ConnectionStatusSuspended = "suspended";

    // ── Media restart reasons ─────────────────────────────────

    /// <summary><c>"stalled outbound media"</c></summary>
    public const string MediaRestartStalledOutbound = "stalled outbound media";

    /// <summary><c>"local track negotiation"</c> — lighter negotiation-only request.</summary>
    public const string MediaRestartLocalTrackNegotiation = "local track negotiation";

    // ── Relay failed reasons ──────────────────────────────────

    /// <summary><c>"target_suspended"</c></summary>
    public const string RelayFailedTargetSuspended = "target_suspended";
}
