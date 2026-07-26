using System.Net.WebSockets;
using System.Text;

namespace Serenada.Core.Signaling;

/// <summary>
/// WebSocket signaling transport implementation using
/// <see cref="System.Net.WebSockets.ClientWebSocket"/>.
/// No external dependencies required.
/// </summary>
internal class WebSocketSignalingTransport : ISignalingTransport
{
    private ClientWebSocket? _ws;
    private CancellationTokenSource? _receiveCts;
    private readonly int _connectTimeoutMs;
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    public TransportKind Kind => TransportKind.Ws;

    public bool IsOpen => _ws?.State == WebSocketState.Open;

    public WebSocketSignalingTransport(int connectTimeoutMs = WebRtcResilienceConstants.ConnectTimeoutMs)
    {
        _connectTimeoutMs = connectTimeoutMs;
    }

    public async Task ConnectAsync(
        string host,
        Action<string> onOpen,
        Action<SignalingMessage> onMessage,
        Action<string> onClosed,
        CancellationToken ct = default)
    {
        _ws?.Dispose();
        _ws = new ClientWebSocket();

        try
        {
            var uri = new Uri(Networking.CoreApiClient.WsUrl(host));

            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            connectCts.CancelAfter(_connectTimeoutMs);

            await _ws.ConnectAsync(uri, connectCts.Token);

            onOpen("ws");

            _receiveCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _ = ReceiveLoopAsync(_ws, onMessage, onClosed, _receiveCts.Token);
        }
        catch (OperationCanceledException)
        {
            onClosed(ct.IsCancellationRequested ? "cancelled" : "connect_timeout");
        }
        catch (Exception ex)
        {
            onClosed($"connect_error: {ex.Message}");
        }
    }

    public async Task SendAsync(SignalingMessage message, CancellationToken ct = default)
    {
        var json = message.ToJson();
        var bytes = Encoding.UTF8.GetBytes(json);

        var lockTaken = false;
        try
        {
            await _sendLock.WaitAsync(ct);
            lockTaken = true;
            var socket = _ws;
            if (socket is not { State: WebSocketState.Open })
                return;

            await socket.SendAsync(
                new ArraySegment<byte>(bytes),
                WebSocketMessageType.Text,
                endOfMessage: true,
                ct);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WS] Send error: {ex.Message}");
        }
        finally
        {
            if (lockTaken)
                _sendLock.Release();
        }
    }

    public async Task CloseAsync(string reason = "client_close")
    {
        _receiveCts?.Cancel();

        await _sendLock.WaitAsync();
        try
        {
            if (_ws is { State: WebSocketState.Open or WebSocketState.CloseReceived })
            {
                try
                {
                    using var closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    await _ws.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        reason,
                        closeCts.Token);
                }
                catch
                {
                    // Best-effort close
                }
            }

            _ws?.Dispose();
            _ws = null;
        }
        finally
        {
            _sendLock.Release();
        }
    }

    // ── Receive loop ──────────────────────────────────────────

    private static async Task ReceiveLoopAsync(
        ClientWebSocket ws,
        Action<SignalingMessage> onMessage,
        Action<string> onClosed,
        CancellationToken ct)
    {
        var buffer = new byte[65536]; // 64KB max message size
        var textBuffer = new StringBuilder();

        try
        {
            while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    onClosed("server_closed");
                    return;
                }

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    textBuffer.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));

                    if (result.EndOfMessage)
                    {
                        var json = textBuffer.ToString();
                        textBuffer.Clear();

                        var msg = SignalingMessage.FromJson(json);
                        if (msg != null)
                        {
                            onMessage(msg);
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            onClosed("cancelled");
        }
        catch (WebSocketException ex)
        {
            onClosed($"ws_error: {ex.Message}");
        }
        catch (Exception ex)
        {
            onClosed($"error: {ex.Message}");
        }
    }
}
