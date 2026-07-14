namespace Serenada.Core.Signaling;

/// <summary>
/// Transport kind identifier.
/// </summary>
public enum TransportKind
{
    /// <summary>WebSocket transport.</summary>
    Ws,

    /// <summary>Server-Sent Events transport.</summary>
    Sse,
}

/// <summary>
/// Low-level signaling transport interface. Wraps a single connection
/// (WebSocket or SSE) to the Serenada server.
/// </summary>
internal interface ISignalingTransport
{
    /// <summary>Which transport this is.</summary>
    TransportKind Kind { get; }

    /// <summary>Whether the transport is currently open and ready to send.</summary>
    bool IsOpen { get; }

    /// <summary>
    /// Connect to the given host.
    /// </summary>
    /// <param name="host">Server host (e.g. "serenada.app" or "localhost:8080").</param>
    /// <param name="onOpen">Called when the transport is ready.</param>
    /// <param name="onMessage">Called for each received <see cref="SignalingMessage"/>.</param>
    /// <param name="onClosed">Called when the transport closes, with a reason string.</param>
    /// <param name="ct">Cancellation token.</param>
    Task ConnectAsync(string host,
        Action<string> onOpen,
        Action<SignalingMessage> onMessage,
        Action<string> onClosed,
        CancellationToken ct = default);

    /// <summary>Send a signaling message.</summary>
    Task SendAsync(SignalingMessage message, CancellationToken ct = default);

    /// <summary>Close the transport gracefully.</summary>
    Task CloseAsync(string reason = "client_close");
}
