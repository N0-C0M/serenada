namespace Serenada.Core.Models;

/// <summary>
/// Low-level real-time diagnostics exposed alongside <see cref="CallState"/>.
/// </summary>
public sealed record CallDiagnostics
{
    /// <summary>Whether the signaling transport is currently connected.</summary>
    public bool IsSignalingConnected { get; init; }

    /// <summary>Aggregate ICE connection state across all peer slots.</summary>
    public string? IceConnectionState { get; init; }

    /// <summary>Aggregate peer connection state across all slots.</summary>
    public string? PeerConnectionState { get; init; }

    /// <summary>Aggregate RTC signaling state across all slots.</summary>
    public string? RtcSignalingState { get; init; }

    /// <summary>Active signaling transport kind, or <c>null</c>.</summary>
    public string? ActiveTransport { get; init; }

    /// <summary>High-level WebRTC stats snapshot.</summary>
    public CallStats? CallStats { get; init; }

    /// <summary>Whether the front (selfie) camera is active.</summary>
    public bool IsFrontCamera { get; init; } = true;

    /// <summary>Whether screen sharing is currently active.</summary>
    public bool IsScreenSharing { get; init; }

    /// <summary>Camera zoom factor.</summary>
    public float CameraZoomFactor { get; init; } = 1.0f;

    /// <summary>Whether flash is available on the current camera.</summary>
    public bool IsFlashAvailable { get; init; }

    /// <summary>Whether flash is currently enabled.</summary>
    public bool IsFlashEnabled { get; init; }

    /// <summary>CID of the remote participant currently sharing content, if any.</summary>
    public string? RemoteContentParticipantId { get; init; }

    /// <summary>Content type being shared, e.g. "screenShare".</summary>
    public string? RemoteContentType { get; init; }

    /// <summary>Whether composite camera is unavailable on this device.</summary>
    public bool CompositeCameraUnavailable { get; init; }
}
