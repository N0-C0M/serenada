using System.Net.Http;
using System.Security.Cryptography;
using System.Text;

namespace Serenada.Core.Signaling;

/// <summary>
/// Server-Sent Events signaling transport implementation.
/// Uses a streaming HTTP GET for receiving and HTTP POST for sending.
/// Full implementation in Phase 4 (SSE fallback + resilience).
/// </summary>
internal class SseSignalingTransport : ISignalingTransport
{
    private HttpClient? _httpClient;
    private CancellationTokenSource? _receiveCts;
    private string? _sessionId;
    private string? _host;

    public TransportKind Kind => TransportKind.Sse;

    public bool IsOpen => _httpClient != null;

    public async Task ConnectAsync(
        string host,
        Action<string> onOpen,
        Action<SignalingMessage> onMessage,
        Action<string> onClosed,
        CancellationToken ct = default)
    {
        _host = host;
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("Accept", "text/event-stream");

        // Generate a session ID for SSE
        _sessionId = GenerateSessionId();

        var url = $"{Networking.CoreApiClient.SseUrl(host)}?sid={_sessionId}";

        try
        {
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            connectCts.CancelAfter(WebRtcResilienceConstants.ConnectTimeoutMs);

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                connectCts.Token);

            response.EnsureSuccessStatusCode();

            onOpen("sse");

            _receiveCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _ = ReceiveLoopAsync(response, onMessage, onClosed, _receiveCts.Token);
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
        if (_httpClient == null || _host == null)
            return;

        // Add session ID to outbound messages
        message = message with { Sid = message.Sid ?? _sessionId };

        var json = message.ToJson();
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        try
        {
            var postUrl = $"{Networking.CoreApiClient.SseUrl(_host)}?sid={_sessionId}";
            var response = await _httpClient.PostAsync(postUrl, content, ct);

            // 410 Gone means the SSE session expired — signal transport closure
            if (response.StatusCode == System.Net.HttpStatusCode.Gone)
            {
                await CloseAsync("session_expired");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SSE] Send error: {ex.Message}");
        }
    }

    public async Task CloseAsync(string reason = "client_close")
    {
        _receiveCts?.Cancel();
        _httpClient?.Dispose();
        _httpClient = null;
        await Task.CompletedTask;
    }

    // ── SSE receive loop ─────────────────────────────────────

    private static async Task ReceiveLoopAsync(
        HttpResponseMessage response,
        Action<SignalingMessage> onMessage,
        Action<string> onClosed,
        CancellationToken ct)
    {
        try
        {
            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream, Encoding.UTF8);

            string? currentEvent = null;
            var dataBuffer = new StringBuilder();

            while (!ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct);

                if (line == null)
                {
                    // Stream ended
                    onClosed("stream_ended");
                    return;
                }

                if (line.StartsWith("event:"))
                {
                    currentEvent = line["event:".Length..].Trim();
                }
                else if (line.StartsWith("data:"))
                {
                    dataBuffer.Append(line["data:".Length..].Trim());
                }
                else if (line.Length == 0)
                {
                    // Empty line = end of event
                    if (dataBuffer.Length > 0)
                    {
                        var json = dataBuffer.ToString();
                        dataBuffer.Clear();

                        var msg = SignalingMessage.FromJson(json);
                        if (msg != null)
                        {
                            onMessage(msg);
                        }
                    }
                    currentEvent = null;
                }
            }

            onClosed("cancelled");
        }
        catch (OperationCanceledException)
        {
            onClosed("cancelled");
        }
        catch (Exception ex)
        {
            onClosed($"sse_error: {ex.Message}");
        }
    }

    private static string GenerateSessionId()
    {
        var bytes = new byte[16];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
