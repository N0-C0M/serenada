namespace Serenada.Core.Models;

/// <summary>
/// Error codes for call failures.
/// </summary>
public enum CallErrorCode
{
    /// <summary>Signaling connection timed out.</summary>
    SignalingTimeout,

    /// <summary>WebRTC connection failed.</summary>
    ConnectionFailed,

    /// <summary>Room is at capacity.</summary>
    RoomFull,

    /// <summary>Room was ended by host or server.</summary>
    RoomEnded,

    /// <summary>Persisted reconnect credential expired or was rejected.</summary>
    SessionExpired,

    /// <summary>Required media permissions were denied.</summary>
    PermissionDenied,

    /// <summary>Server returned an error.</summary>
    ServerError,

    /// <summary>WebRTC is not available on this device.</summary>
    WebRtcUnavailable,

    /// <summary>Media devices are unavailable.</summary>
    MediaUnavailable,

    /// <summary>An unknown error occurred.</summary>
    Unknown,
}

/// <summary>
/// Error with a machine-readable code and human-readable message.
/// </summary>
/// <param name="Code">Machine-readable error code.</param>
/// <param name="Message">Human-readable description.</param>
public sealed record CallError(CallErrorCode Code, string Message);
