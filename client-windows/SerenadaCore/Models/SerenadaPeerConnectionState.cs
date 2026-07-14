namespace Serenada.Core.Models;

/// <summary>
/// WebRTC peer connection state for a remote participant.
/// Mirrors <c>RTCPeerConnectionState</c>.
/// </summary>
public enum SerenadaPeerConnectionState
{
    /// <summary>Connection is newly created.</summary>
    New,

    /// <summary>Connection is being established.</summary>
    Connecting,

    /// <summary>Connection is active and media is flowing.</summary>
    Connected,

    /// <summary>Connection is disconnected.</summary>
    Disconnected,

    /// <summary>Connection has failed.</summary>
    Failed,

    /// <summary>Connection is closed.</summary>
    Closed,
}
