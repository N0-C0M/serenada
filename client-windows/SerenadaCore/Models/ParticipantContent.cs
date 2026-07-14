namespace Serenada.Core.Models;

/// <summary>
/// Active content (screen share) presentation state for a participant.
/// Surfaced on both <see cref="RemoteParticipant"/> and <see cref="LocalParticipant"/>.
/// <c>null</c> when the participant is not currently sharing content.
/// </summary>
public sealed record ParticipantContent
{
    /// <summary><c>true</c> while the participant is presenting content.</summary>
    public bool Active { get; init; }

    /// <summary>Content kind. Currently always <c>"screenShare"</c>.</summary>
    public string Type { get; init; } = "screenShare";

    /// <summary>
    /// Per-participant monotonic generation marker for the content state, scoped
    /// to the sender's current session. Orders presentation-state changes; does
    /// not bind RTP media to a share.
    /// </summary>
    public long Revision { get; init; }
}
