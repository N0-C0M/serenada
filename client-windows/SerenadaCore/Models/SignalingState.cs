namespace Serenada.Core.Models;

/// <summary>
/// Richer view of the local signaling transport state.
/// </summary>
public abstract record SignalingState
{
    /// <summary>Transport is connected and healthy.</summary>
    public sealed record Connected : SignalingState;

    /// <summary>
    /// Actively retrying to (re)connect.
    /// </summary>
    /// <param name="Attempt">Consecutive reconnect attempts since the transport last dropped.</param>
    /// <param name="NextRetryAtMs">
    /// Wall-clock ms for the next scheduled retry, or <c>null</c> if a retry is in flight.
    /// </param>
    public sealed record Reconnecting(int Attempt, long? NextRetryAtMs) : SignalingState;

    /// <summary>
    /// Mid-call transport drop. The server is holding the participant slot for the
    /// hard-eviction window; apps can render a countdown.
    /// </summary>
    /// <param name="SuspendedSinceMs">Wall-clock ms when the local transport last dropped.</param>
    /// <param name="EstimatedHardEvictionAtMs">
    /// Computed locally from <c>SuspendedSinceMs + SUSPEND_HARD_EVICTION_TIMEOUT_MS</c>.
    /// </param>
    public sealed record Suspended(long SuspendedSinceMs, long EstimatedHardEvictionAtMs) : SignalingState;

    /// <summary>Terminal failure.</summary>
    /// <param name="Reason">The error that caused the failure.</param>
    public sealed record Failed(CallError Reason) : SignalingState;
}
