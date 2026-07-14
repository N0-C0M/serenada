using Serenada.Core.Models;

namespace Serenada.Core.Call;

/// <summary>
/// Periodically broadcasts <c>media_liveness</c> hints to the signaling server
/// for remote CIDs whose inbound media is currently flowing.
///
/// The server uses this hint to defer hard-eviction of suspended peers whose
/// media is still being received, even if their signaling transport is late
/// to recover.
///
/// Mirrors the cross-platform media_liveness emission on Android, iOS, and Web.
/// </summary>
internal class MediaLivenessEmitter : IDisposable
{
    private readonly Func<IReadOnlyList<string>> _getActiveCids;
    private readonly Action<object> _broadcast;
    private readonly ISerenadaLogger? _logger;

    private CancellationTokenSource? _cts;
    private bool _disposed;

    /// <param name="getActiveCids">
    /// Returns CIDs of remote participants whose inbound media is currently flowing.
    /// </param>
    /// <param name="broadcast">
    /// Broadcasts a signaling message to the server.
    /// </param>
    public MediaLivenessEmitter(
        Func<IReadOnlyList<string>> getActiveCids,
        Action<object> broadcast,
        ISerenadaLogger? logger)
    {
        _getActiveCids = getActiveCids;
        _broadcast = broadcast;
        _logger = logger;
    }

    /// <summary>
    /// Start periodic emission. Safe to call multiple times — restarts the timer.
    /// </summary>
    public void Start()
    {
        Stop();
        _cts = new CancellationTokenSource();
        _ = EmitLoopAsync(_cts.Token);
    }

    /// <summary>
    /// Stop emission.
    /// </summary>
    public void Stop()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    /// <summary>
    /// Emit immediately (used after reconnect to refresh server state).
    /// </summary>
    public void EmitNow()
    {
        var cids = _getActiveCids();
        if (cids.Count == 0) return;

        _broadcast(new
        {
            type = "media_liveness",
            payload = new { cids },
        });

        Log(SerenadaLogLevel.Debug, "Liveness", $"Emitted for CIDs: [{string.Join(",", cids)}]");
    }

    private async Task EmitLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(WebRtcResilienceConstants.MediaLivenessIntervalMs, ct);
                EmitNow();
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Log(SerenadaLogLevel.Warning, "Liveness", $"Emit failed: {ex.Message}");
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }

    private void Log(SerenadaLogLevel level, string tag, string message)
    {
        _logger?.Log(level, tag, message);
    }
}
