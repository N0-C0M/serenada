using Serenada.Core.Models;

namespace Serenada.Core.Call;

/// <summary>
/// Coordinates the join flow with three timed phases:
/// 1. Join Connect Kickstart (1200ms) — kick-start signaling if not connected
/// 2. Join Recovery (4000ms) — re-send join if not acknowledged
/// 3. Join Hard Timeout (15000ms) — abandon join
///
/// Mirrors <c>JoinFlowCoordinator</c> on Android and iOS.
/// </summary>
internal class JoinFlowCoordinator
{
    private readonly Action<IReadOnlyList<MediaCapability>> _onRequestPermissions;
    private readonly Action _onStartJoin;
    private readonly Action _onSendJoin;
    private readonly Action _onTimeout;
    private readonly Action _onEnsureConnection;
    private readonly Action<SerenadaLogLevel, string, string> _log;

    private CancellationTokenSource? _kickstartCts;
    private CancellationTokenSource? _recoveryCts;
    private CancellationTokenSource? _hardTimeoutCts;
    private bool _joinAcknowledged;
    private bool _isConnected;
    private int _joinAttempt;

    public JoinFlowCoordinator(
        Action<IReadOnlyList<MediaCapability>> onRequestPermissions,
        Action onStartJoin,
        Action onSendJoin,
        Action onTimeout,
        Action onEnsureConnection,
        Action<SerenadaLogLevel, string, string> log)
    {
        _onRequestPermissions = onRequestPermissions;
        _onStartJoin = onStartJoin;
        _onSendJoin = onSendJoin;
        _onTimeout = onTimeout;
        _onEnsureConnection = onEnsureConnection;
        _log = log;
    }

    /// <summary>Begin the join flow.</summary>
    public void Begin()
    {
        Cancel();
        _joinAcknowledged = false;
        _joinAttempt = 0;

        _kickstartCts = new CancellationTokenSource();
        _recoveryCts = new CancellationTokenSource();
        _hardTimeoutCts = new CancellationTokenSource();

        _onStartJoin();

        // Schedule timers
        _ = ScheduleKickstartAsync(_kickstartCts.Token);
        _ = ScheduleRecoveryAsync(_recoveryCts.Token);
        _ = ScheduleHardTimeoutAsync(_hardTimeoutCts.Token);
    }

    /// <summary>Called when the signaling transport opens.</summary>
    public void OnConnected()
    {
        _isConnected = true;
        // Send join immediately
        _joinAttempt++;
        _onSendJoin();
    }

    /// <summary>Called when the signaling transport closes.</summary>
    public void OnDisconnected()
    {
        _isConnected = false;
    }

    /// <summary>Called when the server acknowledges the join (joined message received).</summary>
    public void OnJoinAcknowledged()
    {
        _joinAcknowledged = true;
        Cancel();
    }

    /// <summary>Cancel all timers.</summary>
    public void Cancel()
    {
        _kickstartCts?.Cancel();
        _recoveryCts?.Cancel();
        _hardTimeoutCts?.Cancel();
        _kickstartCts?.Dispose();
        _recoveryCts?.Dispose();
        _hardTimeoutCts?.Dispose();
        _kickstartCts = null;
        _recoveryCts = null;
        _hardTimeoutCts = null;
    }

    // ── Timer implementations ─────────────────────────────────

    private async Task ScheduleKickstartAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(WebRtcResilienceConstants.JoinConnectKickstartMs, ct);

            if (_joinAcknowledged || ct.IsCancellationRequested)
                return;

            if (!_isConnected)
            {
                _log(SerenadaLogLevel.Debug, "JoinFlow", "Kickstart: ensuring signaling connection.");
                _onEnsureConnection();
            }
        }
        catch (OperationCanceledException) { /* Expected */ }
    }

    private async Task ScheduleRecoveryAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(WebRtcResilienceConstants.JoinRecoveryMs, ct);

            if (_joinAcknowledged || ct.IsCancellationRequested)
                return;

            _log(SerenadaLogLevel.Warning, "JoinFlow", "Recovery: re-sending join.");
            _joinAttempt++;
            if (!_isConnected)
            {
                _onEnsureConnection();
            }
            _onSendJoin();
        }
        catch (OperationCanceledException) { /* Expected */ }
    }

    private async Task ScheduleHardTimeoutAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(WebRtcResilienceConstants.JoinHardTimeoutMs, ct);

            if (_joinAcknowledged || ct.IsCancellationRequested)
                return;

            _log(SerenadaLogLevel.Error, "JoinFlow", "Hard timeout: abandoning join.");
            _onTimeout();
        }
        catch (OperationCanceledException) { /* Expected */ }
    }
}
