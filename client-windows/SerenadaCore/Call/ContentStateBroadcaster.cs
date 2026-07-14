using Serenada.Core.Models;
using Serenada.Core.Signaling;

namespace Serenada.Core.Call;

/// <summary>
/// Manages outbound <c>content_state</c> messages for screen share presentation state.
/// Tracks a monotonically increasing revision counter per session, scoped to the
/// sender's session identity.
///
/// Mirrors the cross-platform content_state handling on Android, iOS, and Web.
/// </summary>
internal class ContentStateBroadcaster
{
    private readonly Action<string, object?> _broadcast;
    private readonly ISerenadaLogger? _logger;

    private long _revision;
    private bool _active;
    private string _contentType = SignalingProtocolConstants.ContentTypeScreenShare;

    public bool IsActive => _active;

    public ContentStateBroadcaster(
        Action<string, object?> broadcast,
        ISerenadaLogger? logger)
    {
        _broadcast = broadcast;
        _logger = logger;
    }

    /// <summary>
    /// Announce that screen sharing has started or changed.
    /// </summary>
    public void StartSharing(string contentType = SignalingProtocolConstants.ContentTypeScreenShare)
    {
        _revision++;
        _active = true;
        _contentType = contentType;

        Broadcast(new
        {
            active = true,
            contentType,
            revision = _revision,
        });

        Log(SerenadaLogLevel.Info, "ContentState", $"Sharing started (rev={_revision}).");
    }

    /// <summary>
    /// Announce that screen sharing has stopped.
    /// </summary>
    public void StopSharing()
    {
        if (!_active) return;

        _revision++;
        _active = false;

        Broadcast(new
        {
            active = false,
            revision = _revision,
        });

        Log(SerenadaLogLevel.Info, "ContentState", $"Sharing stopped (rev={_revision}).");
    }

    /// <summary>
    /// Seed the outgoing revision counter past a server-persisted snapshot
    /// (used when reconnecting to a room where our previous share state was preserved).
    /// </summary>
    public void SeedRevision(long serverRevision)
    {
        if (serverRevision >= _revision)
            _revision = serverRevision;
    }

    private void Broadcast(object payload)
    {
        _broadcast(SignalingProtocolConstants.TypeContentState, payload);
    }

    private void Log(SerenadaLogLevel level, string tag, string message)
    {
        _logger?.Log(level, tag, message);
    }
}
