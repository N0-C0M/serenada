using Serenada.Core.Models;

namespace Serenada.Core.Signaling;

/// <summary>
/// Dual-transport signaling engine with automatic WebSocket → SSE fallback,
/// ping/pong keepalive, and exponential backoff reconnection.
///
/// Mirrors <c>SignalingClient</c>/<c>SignalingEngine</c> on Android/iOS/Web.
/// </summary>
internal class SignalingEngine : IDisposable
{
    private readonly string _serverHost;
    private readonly IReadOnlyList<SerenadaTransport> _transports;
    private readonly ISerenadaLogger? _logger;

    private ISignalingTransport? _currentTransport;
    private int _transportIndex;
    private int _wsConsecutiveFailures;
    private CancellationTokenSource? _pingCts;
    private CancellationTokenSource? _lifecycleCts;
    private int _missedPongs;
    private bool _transportEverConnected;
    private bool _isExplicitlyDisconnected;
    private long _transportId;

    // Backoff state
    private int _reconnectAttempt;
    private CancellationTokenSource? _reconnectCts;

    // ── Events ────────────────────────────────────────────────

    /// <summary>Raised when a transport opens.</summary>
    public event Action<string>? OnOpen;

    /// <summary>Raised for each received signaling message.</summary>
    public event Action<SignalingMessage>? OnMessage;

    /// <summary>Raised when the transport closes.</summary>
    public event Action<string>? OnClosed;

    // ── Construction ──────────────────────────────────────────

    public SignalingEngine(
        string serverHost,
        IReadOnlyList<SerenadaTransport> transports,
        ISerenadaLogger? logger)
    {
        _serverHost = serverHost;
        _transports = transports.Count > 0 ? transports : [SerenadaTransport.Ws, SerenadaTransport.Sse];
        _logger = logger;
        _lifecycleCts = new CancellationTokenSource();
    }

    // ── Public API ────────────────────────────────────────────

    public async Task ConnectAsync()
    {
        _isExplicitlyDisconnected = false;
        _reconnectAttempt = 0;
        _transportIndex = 0;
        _transportEverConnected = false;
        _wsConsecutiveFailures = 0;

        await TryConnectCurrentTransportAsync();
    }

    public void Send(SignalingMessage message)
    {
        _ = _currentTransport?.SendAsync(message);
    }

    public void Disconnect()
    {
        _isExplicitlyDisconnected = true;
        CancelTimers();
        _ = _currentTransport?.CloseAsync("client_disconnect");
    }

    public void Dispose()
    {
        _isExplicitlyDisconnected = true;
        CancelTimers();
        _lifecycleCts?.Cancel();
        _lifecycleCts?.Dispose();
        _ = _currentTransport?.CloseAsync("dispose");
        _currentTransport = null;
    }

    // ── Internal ──────────────────────────────────────────────

    private async Task TryConnectCurrentTransportAsync()
    {
        _pingCts?.Cancel();
        _pingCts?.Dispose();
        _pingCts = null;

        _lifecycleCts?.Cancel();
        _lifecycleCts?.Dispose();
        _lifecycleCts = new CancellationTokenSource();

        var previousTransport = _currentTransport;
        var transportId = ++_transportId;

        var transportKind = _transportIndex < _transports.Count
            ? _transports[_transportIndex]
            : SerenadaTransport.Ws;

        Log(SerenadaLogLevel.Info, "Engine", $"Connecting via {transportKind} (attempt {_reconnectAttempt + 1})...");

        ISignalingTransport transport = transportKind switch
        {
            SerenadaTransport.Ws => new WebSocketSignalingTransport(),
            SerenadaTransport.Sse => new SseSignalingTransport(),
            _ => new WebSocketSignalingTransport(),
        };
        _currentTransport = transport;

        if (previousTransport != null)
            _ = previousTransport.CloseAsync("transport_replaced");

        await transport.ConnectAsync(
            host: _serverHost,
            onOpen: kind =>
            {
                if (transportId == _transportId)
                    HandleTransportOpen(kind);
            },
            onMessage: message =>
            {
                if (transportId == _transportId)
                    HandleMessage(message);
            },
            onClosed: reason =>
            {
                if (transportId == _transportId)
                    HandleTransportClosed(reason);
            },
            ct: _lifecycleCts.Token);
    }

    private void HandleTransportOpen(string transportKind)
    {
        _transportEverConnected = true;
        _wsConsecutiveFailures = 0;
        _missedPongs = 0;

        Log(SerenadaLogLevel.Info, "Engine", $"Transport opened: {transportKind}.");

        // Start ping/pong keepalive
        _pingCts = CancellationTokenSource.CreateLinkedTokenSource(_lifecycleCts!.Token);
        _ = PingLoopAsync(_pingCts.Token);

        OnOpen?.Invoke(transportKind);
    }

    private void HandleMessage(SignalingMessage msg)
    {
        // Track pongs for keepalive
        if (msg.Type == SignalingProtocolConstants.TypePong)
        {
            _missedPongs = 0;
        }

        OnMessage?.Invoke(msg);
    }

    private void HandleTransportClosed(string reason)
    {
        Log(SerenadaLogLevel.Warning, "Engine", $"Transport closed: {reason}.");

        _pingCts?.Cancel();
        _pingCts?.Dispose();
        _pingCts = null;

        if (reason.Contains("timeout") || reason.Contains("error") || reason.Contains("unsupported"))
        {
            if (_currentTransport?.Kind == TransportKind.Ws)
                _wsConsecutiveFailures++;
        }

        OnClosed?.Invoke(reason);

        // A call was intentionally left or disposed.  Do not resurrect its
        // signaling transport in the background: it can otherwise rejoin the
        // old room after the user has already entered a different one.
        if (_isExplicitlyDisconnected)
            return;

        // Attempt reconnection
        _ = AttemptReconnectAsync(reason);
    }

    private async Task AttemptReconnectAsync(string reason)
    {
        if (_isExplicitlyDisconnected || _reconnectCts != null)
            return;

        // Determine if we should fall back to the next transport
        var shouldFallback = !_transportEverConnected
            || reason.Contains("unsupported")
            || reason.Contains("timeout")
            || (_currentTransport?.Kind == TransportKind.Ws
                && _wsConsecutiveFailures >= WebRtcResilienceConstants.WsFallbackConsecutiveFailures);

        var currentKind = _currentTransport?.Kind;
        var currentTransportMatches = currentKind == TransportKind.Ws && _transports[_transportIndex] == SerenadaTransport.Ws
            || currentKind == TransportKind.Sse && _transports[_transportIndex] == SerenadaTransport.Sse;

        var fallback = shouldFallback
            && _transportIndex + 1 < _transports.Count
            && currentTransportMatches;
        if (fallback)
        {
            _transportIndex++;
            _reconnectAttempt = 0;
            Log(SerenadaLogLevel.Info, "Engine", $"Falling back to {_transports[_transportIndex]}.");
        }
        else
        {
            _reconnectAttempt++;
            _transportIndex = 0;
        }

        // Compute backoff delay
        var exponent = Math.Max(0, _reconnectAttempt - 1);
        var delayMs = (int)Math.Min(
            WebRtcResilienceConstants.ReconnectBackoffBaseMs * Math.Pow(2, exponent),
            WebRtcResilienceConstants.ReconnectBackoffCapMs);

        Log(SerenadaLogLevel.Debug, "Engine", $"Reconnecting in {delayMs}ms (attempt {_reconnectAttempt})...");

        try
        {
            var reconnectCts = new CancellationTokenSource();
            _reconnectCts = reconnectCts;
            await Task.Delay(delayMs, reconnectCts.Token);
            if (_isExplicitlyDisconnected)
                return;

            _reconnectCts = null;
            reconnectCts.Dispose();
            await TryConnectCurrentTransportAsync();
        }
        catch (OperationCanceledException)
        {
            // Reconnect was cancelled
        }
        finally
        {
            _reconnectCts?.Dispose();
            _reconnectCts = null;
        }
    }

    // ── Ping/Pong Keepalive ───────────────────────────────────

    private async Task PingLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(WebRtcResilienceConstants.PingIntervalMs, ct);

                // Send ping
                var pingMsg = SignalingMessage.Outbound(SignalingProtocolConstants.TypePing,
                    payload: new { ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() });
                await _currentTransport!.SendAsync(pingMsg, ct);

                _missedPongs++;

                if (_missedPongs >= WebRtcResilienceConstants.PongMissThreshold)
                {
                    Log(SerenadaLogLevel.Warning, "Engine",
                        $"Missed {_missedPongs} pongs — closing transport.");
                    await _currentTransport.CloseAsync("pong_timeout");
                    return;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Log(SerenadaLogLevel.Warning, "Engine", $"Ping error: {ex.Message}");
                await _currentTransport!.CloseAsync("ping_error");
                break;
            }
        }
    }

    private void CancelTimers()
    {
        _pingCts?.Cancel();
        _pingCts?.Dispose();
        _pingCts = null;

        _reconnectCts?.Cancel();
        _reconnectCts?.Dispose();
        _reconnectCts = null;
    }

    private void Log(SerenadaLogLevel level, string tag, string message)
    {
        _logger?.Log(level, tag, message);
    }
}
