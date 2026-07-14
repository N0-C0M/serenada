using Serenada.Core.Models;

namespace Serenada.Core.Signaling;

/// <summary>
/// Join options passed to <see cref="ISignalingProvider.JoinRoom"/>.
/// </summary>
public sealed record JoinOptions
{
    /// <summary>Device kind: "desktop", "android", "ios", or "unknown".</summary>
    public string Device { get; init; } = "desktop";

    /// <summary>Optional user-agent string.</summary>
    public string? Ua { get; init; }

    /// <summary>Maximum participants this client supports.</summary>
    public int MaxParticipants { get; init; } = 4;

    /// <summary>Optional display name.</summary>
    public string? DisplayName { get; init; }

    /// <summary>Host-supplied stable identity.</summary>
    public string? PeerId { get; init; }

    /// <summary>Previous client ID for reconnection.</summary>
    public string? ReconnectCid { get; init; }

    /// <summary>Opaque reconnect token for identity recovery.</summary>
    public string? ReconnectToken { get; init; }

    /// <summary>Whether this client supports independent content video.</summary>
    public bool IndependentContentVideo { get; init; }

    /// <summary>Whether any video media is negotiated at all.</summary>
    public bool VideoMediaEnabled { get; init; } = true;

    /// <summary>
    /// When creating a new room, request this many max participants.
    /// Clamped to the client's own <see cref="MaxParticipants"/> and the server ceiling.
    /// </summary>
    public int CreateMaxParticipants { get; init; } = 2;
}

/// <summary>
/// Capabilities the signaling provider advertises to the session.
/// </summary>
public sealed record ProviderCapabilities
{
    /// <summary>
    /// When <c>true</c>, the provider manages transport reconnection internally.
    /// The session should not attempt its own reconnect loop on top.
    /// </summary>
    public bool HandlesReconnection { get; init; } = true;
}

/// <summary>
/// Connection info reported on connect.
/// </summary>
public sealed record ConnectionInfo
{
    public string? Transport { get; init; }
}

/// <summary>
/// Peer message relayed from another participant.
/// </summary>
public sealed record PeerMessage
{
    public string Type { get; init; } = string.Empty;
    public string From { get; init; } = string.Empty;
    public string? Sid { get; init; }
    public System.Text.Json.JsonElement? Payload { get; init; }
}

/// <summary>
/// Transport-agnostic signaling provider interface.
/// Decouples session logic from the signaling transport implementation.
/// Mirrors the cross-platform <c>SignalingProvider</c> contract.
/// </summary>
public interface ISignalingProvider
{
    /// <summary>Protocol version. Must be <see cref="SignalingProviderBase.SupportedVersion"/>.</summary>
    int Version { get; }

    /// <summary>Capabilities of this provider.</summary>
    ProviderCapabilities Capabilities { get; }

    /// <summary>Connect to the signaling server.</summary>
    void Connect();

    /// <summary>Disconnect from the signaling server.</summary>
    void Disconnect();

    /// <summary>Join a room.</summary>
    void JoinRoom(string roomId, JoinOptions options);

    /// <summary>Leave the current room.</summary>
    void LeaveRoom();

    /// <summary>Host ends the room for everyone.</summary>
    void EndRoom();

    /// <summary>Send a directed message to a specific peer.</summary>
    void SendToPeer(string peerId, string type, object? payload);

    /// <summary>Broadcast a message to the room.</summary>
    void Broadcast(string type, object? payload);

    /// <summary>Fetch ICE (STUN/TURN) servers.</summary>
    Task<IReadOnlyList<IceServer>> GetIceServersAsync();

    // ── Events ────────────────────────────────────────────────

    /// <summary>Raised when the transport connects.</summary>
    event Action<ConnectionInfo>? OnConnected;

    /// <summary>Raised when the transport disconnects.</summary>
    event Action<string?>? OnDisconnected;

    /// <summary>Raised when the server acknowledges the join.</summary>
    event Action<JoinedPayload>? OnJoined;

    /// <summary>Raised when room state is updated.</summary>
    event Action<RoomStatePayload>? OnRoomStateUpdated;

    /// <summary>Raised when a peer joins the room.</summary>
    event Action<SignalingParticipant>? OnPeerJoined;

    /// <summary>Raised when a peer leaves the room.</summary>
    event Action<SignalingParticipant>? OnPeerLeft;

    /// <summary>Raised for relayed peer messages (offer/answer/ICE/content_state/etc.).</summary>
    event Action<PeerMessage>? OnMessage;

    /// <summary>Raised when the room is ended by the host.</summary>
    event Action<string>? OnRoomEnded;

    /// <summary>Raised on server error.</summary>
    event Action<ErrorPayload>? OnError;

    /// <summary>Raised when ICE servers change (new TURN credentials).</summary>
    event Action<IReadOnlyList<IceServer>>? OnIceServersChanged;

    /// <summary>Raised when a suspended peer reattaches and needs fresh negotiation.</summary>
    event Action<NegotiationDirtyPayload>? OnNegotiationDirty;

    /// <summary>Raised when relay to a suspended peer failed.</summary>
    event Action<RelayFailedPayload>? OnRelayFailed;

    /// <summary>Raised when the reconnect token is refreshed.</summary>
    event Action<ReconnectTokenRefreshedPayload>? OnReconnectTokenRefreshed;
}

/// <summary>
/// ICE server configuration (STUN or TURN).
/// </summary>
public sealed record IceServer
{
    public IReadOnlyList<string> Urls { get; init; } = [];
    public string? Username { get; init; }
    public string? Password { get; init; }
}

/// <summary>
/// Base class for signaling providers with event-raising helpers.
/// </summary>
public abstract class SignalingProviderBase : ISignalingProvider
{
    /// <summary>Protocol version supported by the SDK.</summary>
    public const int SupportedVersion = 1;

    /// <inheritdoc/>
    public abstract int Version { get; }

    /// <inheritdoc/>
    public abstract ProviderCapabilities Capabilities { get; }

    /// <inheritdoc/>
    public abstract void Connect();
    /// <inheritdoc/>
    public abstract void Disconnect();
    /// <inheritdoc/>
    public abstract void JoinRoom(string roomId, JoinOptions options);
    /// <inheritdoc/>
    public abstract void LeaveRoom();
    /// <inheritdoc/>
    public abstract void EndRoom();
    /// <inheritdoc/>
    public abstract void SendToPeer(string peerId, string type, object? payload);
    /// <inheritdoc/>
    public abstract void Broadcast(string type, object? payload);
    /// <inheritdoc/>
    public abstract Task<IReadOnlyList<IceServer>> GetIceServersAsync();

    /// <inheritdoc/>
    public event Action<ConnectionInfo>? OnConnected;
    /// <inheritdoc/>
    public event Action<string?>? OnDisconnected;
    /// <inheritdoc/>
    public event Action<JoinedPayload>? OnJoined;
    /// <inheritdoc/>
    public event Action<RoomStatePayload>? OnRoomStateUpdated;
    /// <inheritdoc/>
    public event Action<SignalingParticipant>? OnPeerJoined;
    /// <inheritdoc/>
    public event Action<SignalingParticipant>? OnPeerLeft;
    /// <inheritdoc/>
    public event Action<PeerMessage>? OnMessage;
    /// <inheritdoc/>
    public event Action<string>? OnRoomEnded;
    /// <inheritdoc/>
    public event Action<ErrorPayload>? OnError;
    /// <inheritdoc/>
    public event Action<IReadOnlyList<IceServer>>? OnIceServersChanged;
    /// <inheritdoc/>
    public event Action<NegotiationDirtyPayload>? OnNegotiationDirty;
    /// <inheritdoc/>
    public event Action<RelayFailedPayload>? OnRelayFailed;
    /// <inheritdoc/>
    public event Action<ReconnectTokenRefreshedPayload>? OnReconnectTokenRefreshed;

    // ── Protected event-raising helpers ───────────────────────

    protected void RaiseConnected(ConnectionInfo info) => OnConnected?.Invoke(info);
    protected void RaiseDisconnected(string? reason) => OnDisconnected?.Invoke(reason);
    protected void RaiseJoined(JoinedPayload payload) => OnJoined?.Invoke(payload);
    protected void RaiseRoomStateUpdated(RoomStatePayload payload) => OnRoomStateUpdated?.Invoke(payload);
    protected void RaisePeerJoined(SignalingParticipant participant) => OnPeerJoined?.Invoke(participant);
    protected void RaisePeerLeft(SignalingParticipant participant) => OnPeerLeft?.Invoke(participant);
    protected void RaiseMessage(PeerMessage message) => OnMessage?.Invoke(message);
    protected void RaiseRoomEnded(string by) => OnRoomEnded?.Invoke(by);
    protected void RaiseError(ErrorPayload error) => OnError?.Invoke(error);
    protected void RaiseIceServersChanged(IReadOnlyList<IceServer> servers) => OnIceServersChanged?.Invoke(servers);
    protected void RaiseNegotiationDirty(NegotiationDirtyPayload payload) => OnNegotiationDirty?.Invoke(payload);
    protected void RaiseRelayFailed(RelayFailedPayload payload) => OnRelayFailed?.Invoke(payload);
    protected void RaiseReconnectTokenRefreshed(ReconnectTokenRefreshedPayload payload) => OnReconnectTokenRefreshed?.Invoke(payload);
}
