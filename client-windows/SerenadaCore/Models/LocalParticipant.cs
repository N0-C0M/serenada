namespace Serenada.Core.Models;

/// <summary>
/// The local participant in a call.
/// </summary>
public sealed record LocalParticipant
{
    /// <summary>Client identifier assigned by the server.</summary>
    public string? Cid { get; init; }

    /// <summary>Display name shown to other participants.</summary>
    public string? DisplayName { get; init; }

    /// <summary>
    /// Host-supplied stable identity passed at join time. Distinct from
    /// <see cref="Cid"/> (per-call, server-issued).
    /// </summary>
    public string? PeerId { get; init; }

    /// <summary>Whether local audio is enabled.</summary>
    public bool AudioEnabled { get; init; } = true;

    /// <summary>Whether local video (camera or content) is enabled.</summary>
    public bool VideoEnabled { get; init; } = true;

    /// <summary>
    /// Whether the local camera specifically is enabled. Currently mirrors
    /// <see cref="VideoEnabled"/>.
    /// </summary>
    public bool CameraEnabled { get; init; } = true;

    /// <summary>Current camera mode.</summary>
    public CameraMode CameraMode { get; init; } = CameraMode.Selfie;

    /// <summary>
    /// Camera modes the user can cycle through, in preference order.
    /// Empty means camera video is unavailable.
    /// </summary>
    public IReadOnlyList<CameraMode> AvailableCameraModes { get; init; } =
        [CameraMode.Selfie, CameraMode.World, CameraMode.Composite];

    /// <summary>Whether this participant is the room host.</summary>
    public bool IsHost { get; init; }

    /// <summary>Smoothed voice activity level (0..1) for the locally captured mic.</summary>
    public float AudioLevel { get; init; }

    /// <summary>Local content (screen-share) state, or <c>null</c> when not sharing.</summary>
    public ParticipantContent? Content { get; init; }
}
