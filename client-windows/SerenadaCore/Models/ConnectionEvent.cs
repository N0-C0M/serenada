namespace Serenada.Core.Models;

/// <summary>
/// Reason a dropout began.
/// </summary>
public enum DropoutTrigger
{
    /// <summary>Signaling or network was lost.</summary>
    NetworkLost,

    /// <summary>Unknown cause.</summary>
    Unknown,
}

/// <summary>
/// Connection-quality event emitted by the SDK.
/// </summary>
public abstract record ConnectionEvent
{
    /// <summary>
    /// Emitted when a dropout has recovered.
    /// </summary>
    /// <param name="DowntimeMs">Downtime of the recovered dropout, in ms.</param>
    /// <param name="Reason">What triggered the dropout.</param>
    public sealed record Reconnected(long DowntimeMs, DropoutTrigger Reason) : ConnectionEvent;

    /// <summary>
    /// Emitted when a reconnection attempt fails terminally.
    /// </summary>
    /// <param name="Reason">Why the reconnect failed.</param>
    public sealed record ReconnectFailed(ReconnectFailedReason Reason) : ConnectionEvent;
}

/// <summary>
/// Reason a reconnection attempt failed.
/// </summary>
public enum ReconnectFailedReason
{
    /// <summary>Recovery window elapsed.</summary>
    Timeout,

    /// <summary>No network connectivity available.</summary>
    NetworkConnectivity,
}
