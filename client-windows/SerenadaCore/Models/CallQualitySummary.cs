namespace Serenada.Core.Models;

/// <summary>
/// Immutable snapshot of aggregate call quality, computed by the SDK and
/// consumed by hosts to populate call-ended analytics.
/// Updated live during the call and finalized at call end.
/// </summary>
public sealed record CallQualitySummary
{
    /// <summary>
    /// MOS estimate (heuristic). <c>null</c> unless all of
    /// <see cref="MedianLatencyMs"/>, <see cref="MedianJitterMs"/>,
    /// and <see cref="PacketLossPct"/> are defined.
    /// </summary>
    public double? MosScore { get; init; }

    /// <summary>
    /// Call-level audio rx packet loss percentage, computed from counter deltas
    /// over the in-call window.
    /// </summary>
    public double? PacketLossPct { get; init; }

    /// <summary>Median of sampled RTT in ms.</summary>
    public double? MedianLatencyMs { get; init; }

    /// <summary>Median of sampled audio jitter in ms.</summary>
    public double? MedianJitterMs { get; init; }

    /// <summary>Number of dropout starts while in-call.</summary>
    public int CountDisconnects { get; init; }

    /// <summary>Number of dropouts that recovered.</summary>
    public int CountReconnects { get; init; }

    /// <summary>Sum of dropout interval durations in ms.</summary>
    public long TotalDropoutDurationMs { get; init; }

    /// <summary>
    /// Count of stats samples that contributed ≥1 usable quality field.
    /// </summary>
    public int QualitySampleCount { get; init; }
}
