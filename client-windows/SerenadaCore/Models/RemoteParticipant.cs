namespace Serenada.Core.Models;

/// <summary>
/// Remote participant in a call.
/// </summary>
public sealed record RemoteParticipant
{
    /// <summary>Client identifier assigned by the server.</summary>
    public string Cid { get; init; } = string.Empty;

    /// <summary>Display name shown to other participants.</summary>
    public string? DisplayName { get; init; }

    /// <summary>
    /// Host-supplied stable identity passed at join time. Distinct from
    /// <see cref="Cid"/> (per-call, server-issued).
    /// </summary>
    public string? PeerId { get; init; }

    /// <summary>Whether remote audio is enabled.</summary>
    public bool AudioEnabled { get; init; } = true;

    /// <summary>Whether remote video (camera or content) is enabled.</summary>
    public bool VideoEnabled { get; init; } = true;

    /// <summary>
    /// Whether the remote camera specifically is enabled. Currently mirrors
    /// <see cref="VideoEnabled"/>; independent screen-share state is carried
    /// by <see cref="Content"/>.
    /// </summary>
    public bool CameraEnabled { get; init; } = true;

    /// <summary>
    /// <c>true</c> while this peer's camera inbound video bytes are advancing.
    /// Only meaningful when the camera is expected/active.
    /// </summary>
    public bool CameraReceiving { get; init; }

    /// <summary>
    /// <c>true</c> while this peer's content (screen share) inbound video bytes
    /// are advancing.
    /// </summary>
    public bool ContentReceiving { get; init; }

    /// <summary>WebRTC peer connection state for this participant.</summary>
    public SerenadaPeerConnectionState ConnectionState { get; init; } = SerenadaPeerConnectionState.New;

    /// <summary>Signaling transport status as reported by the server.</summary>
    public ParticipantSignalingStatus SignalingStatus { get; init; } = ParticipantSignalingStatus.Active;

    /// <summary>
    /// <c>true</c> when this peer has been suspended longer than the UI timeout
    /// and the SDK has flipped its presentation to "presumed lost."
    /// </summary>
    public bool PresumedLost { get; init; }

    /// <summary>Smoothed voice activity level (0..1) for this peer's inbound audio.</summary>
    public float AudioLevel { get; init; }

    /// <summary>Remote content (screen-share) state, or <c>null</c> when not sharing.</summary>
    public ParticipantContent? Content { get; init; }

    /// <summary>
    /// Whether this peer advertised independent content video support at join
    /// (<c>capabilities.independentContentVideo</c>).
    /// </summary>
    public bool SupportsIndependentContentVideo { get; init; }
}
