namespace Serenada.Core.Models;

/// <summary>
/// Camera mode: selfie (front), world (rear), composite (picture-in-picture),
/// or screen share.
/// </summary>
public enum CameraMode
{
    /// <summary>Front-facing (user-facing) camera.</summary>
    Selfie,

    /// <summary>Rear/world-facing camera.</summary>
    World,

    /// <summary>Picture-in-picture with both front and rear cameras.</summary>
    Composite,

    /// <summary>Screen sharing (not a physical camera).</summary>
    ScreenShare,
}
