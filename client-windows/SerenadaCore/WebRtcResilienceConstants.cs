namespace Serenada.Core;

/// <summary>
/// Canonical WebRTC resilience constants shared across all Serenada clients.
/// MUST match the values in <c>constants.ts</c> (Web), <c>WebRtcResilienceConstants.kt</c>
/// (Android), and <c>WebRtcResilienceConstants.swift</c> (iOS).
/// Verified by <c>scripts/check-resilience-constants.mjs</c>.
/// </summary>
public static class WebRtcResilienceConstants
{
    // ── Signaling ──────────────────────────────────────────────

    /// <summary>Base delay for exponential reconnect backoff.</summary>
    public const int ReconnectBackoffBaseMs = 500;

    /// <summary>Maximum reconnect backoff delay.</summary>
    public const int ReconnectBackoffCapMs = 5000;

    /// <summary>Transport connect timeout.</summary>
    public const int ConnectTimeoutMs = 2000;

    /// <summary>WebSocket/SSE ping interval.</summary>
    public const int PingIntervalMs = 12000;

    /// <summary>Consecutive missed pongs before force-close.</summary>
    public const int PongMissThreshold = 2;

    /// <summary>Consecutive WS failures before falling back to SSE.</summary>
    public const int WsFallbackConsecutiveFailures = 3;

    // ── Join ───────────────────────────────────────────────────

    /// <summary>Audio coordinator activation timeout.</summary>
    public const int AudioCoordinatorTimeoutMs = 10000;

    /// <summary>First join retry / kickstart timer.</summary>
    public const int JoinConnectKickstartMs = 1200;

    /// <summary>Join acknowledgement timeout.</summary>
    public const int JoinRecoveryMs = 4000;

    /// <summary>Hard join deadline.</summary>
    public const int JoinHardTimeoutMs = 15000;

    // ── Peer Connection ────────────────────────────────────────

    /// <summary>SDP offer answer timeout.</summary>
    public const int OfferTimeoutMs = 8000;

    /// <summary>Minimum interval between ICE restarts per peer.</summary>
    public const int IceRestartCooldownMs = 10000;

    /// <summary>Maximum buffered ICE candidates per offerId.</summary>
    public const int IceCandidateBufferMax = 50;

    /// <summary>Outbound media watchdog poll interval.</summary>
    public const int OutboundMediaWatchdogIntervalMs = 5000;

    /// <summary>Consecutive outbound media stall samples before recovery action.</summary>
    public const int OutboundMediaStallSamples = 2;

    /// <summary>Cooldown between outbound media recovery actions.</summary>
    public const int OutboundMediaRecoveryCooldownMs = 30000;

    // ── TURN ───────────────────────────────────────────────────

    /// <summary>TURN credential fetch timeout.</summary>
    public const int TurnFetchTimeoutMs = 2000;

    /// <summary>Schedule TURN refresh at this fraction of TTL.</summary>
    public const double TurnRefreshTriggerRatio = 0.8;

    /// <summary>ICE fetch retry delays in ms.</summary>
    public static readonly int[] IceFetchRetryDelaysMs = [0, 1000, 2000, 4000];

    // ── Reconnect Token ────────────────────────────────────────

    /// <summary>Fallback reconnect token TTL (20 min).</summary>
    public const int ReconnectTokenTtlFallbackMs = 1200000;

    /// <summary>Refresh reconnect token 10 min before expiry.</summary>
    public const int ReconnectTokenRefreshLeewayMs = 600000;

    // ── Session / UI ───────────────────────────────────────────

    /// <summary>"Call ended" screen duration.</summary>
    public const int EndingScreenMs = 3000;

    /// <summary>Snapshot prepare timeout.</summary>
    public const int SnapshotPrepareTimeoutMs = 2000;

    // ── Foreground / Doze Recovery ─────────────────────────────

    /// <summary>Foreground force-ping timeout.</summary>
    public const int ForegroundForcePingTimeoutMs = 2000;

    // ── Post-Reconnect Resync ──────────────────────────────────

    /// <summary>Wait for authoritative room_state after reconnect.</summary>
    public const int EpochResyncTimeoutMs = 5000;

    // ── Suspended Peer ─────────────────────────────────────────

    /// <summary>Before flagging remote as "presumed lost" in UI.</summary>
    public const int PeerSuspendedUiTimeoutMs = 30000;

    /// <summary>Server hard-eviction window (10 min).</summary>
    public const int SuspendHardEvictionTimeoutMs = 600000;

    // ── Media Liveness ─────────────────────────────────────────

    /// <summary>Periodic media_liveness emission interval.</summary>
    public const int MediaLivenessIntervalMs = 10000;

    // ── Connection Status ──────────────────────────────────────

    /// <summary>Delay before transitioning from recovering to retrying.</summary>
    public const int ConnectionRetryingDelayMs = 10000;

    // ── Local Video Recovery ───────────────────────────────────

    /// <summary>Minimum hide duration before forced camera refresh.</summary>
    public const int LocalVideoResumeGapMs = 15000;

    /// <summary>Sleep-detection heartbeat interval.</summary>
    public const int LocalVideoHeartbeatIntervalMs = 5000;
}
