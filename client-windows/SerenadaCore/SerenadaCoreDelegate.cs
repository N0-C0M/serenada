using Serenada.Core.Models;

namespace Serenada.Core;

/// <summary>
/// Delegate interface for session lifecycle events.
/// Mirrors <c>SerenadaCoreDelegate</c> on Android and iOS.
/// </summary>
public interface ISerenadaCoreDelegate
{
    /// <summary>Called when permissions are required before joining.</summary>
    void OnPermissionsRequired(SerenadaSession session, IReadOnlyList<MediaCapability> permissions);

    /// <summary>Called on every state change.</summary>
    void OnSessionStateChanged(SerenadaSession session, CallState state);

    /// <summary>Called when the session ends.</summary>
    void OnSessionEnded(SerenadaSession session, EndReason reason);

    /// <summary>Called on connection-quality events (reconnect/dropout).</summary>
    void OnConnectionEvent(SerenadaSession session, ConnectionEvent connectionEvent);
}
