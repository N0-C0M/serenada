using System.Collections.ObjectModel;

namespace Serenada.Core.Models;

/// <summary>
/// Primary observable call state. This is the main state object that consumers
/// subscribe to. Initially defaults to a safe idle state.
/// </summary>
public sealed record CallState
{
    /// <summary>Current call phase.</summary>
    public CallPhase Phase { get; init; } = CallPhase.Idle;

    /// <summary>Room identifier, if joined.</summary>
    public string? RoomId { get; init; }

    /// <summary>Full room URL, if available.</summary>
    public string? RoomUrl { get; init; }

    /// <summary>The local participant. <c>null</c> before joining.</summary>
    public LocalParticipant? LocalParticipant { get; init; }

    /// <summary>Remote participants currently in the call.</summary>
    public IReadOnlyList<RemoteParticipant> RemoteParticipants { get; init; } =
        ReadOnlyCollection<RemoteParticipant>.Empty;

    /// <summary>Overall connection health.</summary>
    public ConnectionStatus ConnectionStatus { get; init; } = ConnectionStatus.Disconnected;

    /// <summary>
    /// Richer signaling-transport state with timing details. Apps that don't
    /// need the extra detail can stick with <see cref="ConnectionStatus"/>.
    /// </summary>
    public SignalingState SignalingState { get; init; } = new SignalingState.Connected();

    /// <summary>Active signaling transport kind, or <c>null</c> when disconnected.</summary>
    public string? ActiveTransport { get; init; }

    /// <summary>Permissions that must be granted before joining, if any.</summary>
    public IReadOnlyList<MediaCapability>? RequiredPermissions { get; init; }

    /// <summary>Current error, if the phase is <see cref="CallPhase.Error"/>.</summary>
    public CallError? Error { get; init; }

    /// <summary>
    /// Wall-clock timestamp in milliseconds when the local participant joined this call.
    /// </summary>
    public long? CallStartedAtMs { get; init; }

    /// <summary>Number of participants currently in the room (including local).</summary>
    public int ParticipantCount { get; init; }
}
