namespace Serenada.Core.Models;

/// <summary>
/// Signaling connection status of a remote participant as reported by the server.
/// </summary>
public enum ParticipantSignalingStatus
{
    /// <summary>
    /// The participant is currently connected to the signaling server.
    /// </summary>
    Active,

    /// <summary>
    /// The participant's signaling transport dropped and the server is holding their
    /// slot open for reconnect. The peer connection to them is intentionally kept
    /// alive — UIs should show a "reconnecting" indicator.
    /// </summary>
    Suspended,
}
