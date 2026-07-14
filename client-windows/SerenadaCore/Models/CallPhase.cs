namespace Serenada.Core.Models;

/// <summary>
/// Current phase of the call lifecycle.
/// Mirrors the cross-platform <c>CallPhase</c> enum on Android, iOS, and Web.
/// </summary>
public enum CallPhase
{
    /// <summary>No active call.</summary>
    Idle,

    /// <summary>Waiting for the user to grant camera/microphone permissions.</summary>
    AwaitingPermissions,

    /// <summary>Connecting to the signaling server and joining the room.</summary>
    Joining,

    /// <summary>Connected and waiting for another participant to join.</summary>
    Waiting,

    /// <summary>Active call with at least one remote participant.</summary>
    InCall,

    /// <summary>Call is ending (brief transition before returning to idle).</summary>
    Ending,

    /// <summary>An error occurred.</summary>
    Error,
}
