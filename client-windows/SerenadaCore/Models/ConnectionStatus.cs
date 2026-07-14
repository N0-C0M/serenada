namespace Serenada.Core.Models;

/// <summary>
/// Overall connection health status between the client and signaling server.
/// </summary>
public enum ConnectionStatus
{
    /// <summary>Fully connected.</summary>
    Connected,

    /// <summary>Temporarily degraded, attempting automatic recovery.</summary>
    Recovering,

    /// <summary>Connection lost, actively retrying.</summary>
    Retrying,

    /// <summary>Terminally disconnected.</summary>
    Disconnected,
}
