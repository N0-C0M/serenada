namespace Serenada.Core.Models;

/// <summary>
/// Source for a video snapshot — either the local stream or a specific remote
/// participant's stream identified by their CID.
/// </summary>
public abstract record SnapshotSource
{
    /// <summary>Capture from the local video stream.</summary>
    public sealed record Local : SnapshotSource;

    /// <summary>Capture from a remote participant's video stream.</summary>
    /// <param name="Cid">The participant's client ID.</param>
    public sealed record Remote(string Cid) : SnapshotSource;
}

/// <summary>
/// Result of a successful video snapshot.
/// </summary>
/// <param name="Width">Frame width in pixels.</param>
/// <param name="Height">Frame height in pixels.</param>
/// <param name="TimestampMs">Wall-clock time the snapshot was captured, from <c>DateTimeOffset.UtcNow</c>.</param>
/// <param name="Source">Which stream the snapshot came from.</param>
/// <param name="Data">JPEG-encoded image bytes.</param>
public sealed record SnapshotResult(
    int Width,
    int Height,
    long TimestampMs,
    SnapshotSource Source,
    byte[] Data
);
