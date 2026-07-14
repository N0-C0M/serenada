namespace Serenada.Core.Models;

/// <summary>
/// Result of creating a new room via <see cref="SerenadaCore.CreateRoomAsync"/>.
/// </summary>
/// <param name="RoomId">The generated room ID.</param>
/// <param name="RoomUrl">Full URL to share with the other participant.</param>
public sealed record CreateRoomResult(string RoomId, string RoomUrl);

/// <summary>
/// Crash-recovery record persisted by the SDK so a killed process can rejoin
/// the same room with the same participant identity.
/// </summary>
/// <param name="RoomId">Room identifier.</param>
/// <param name="Cid">Server-issued client ID from the prior session.</param>
/// <param name="ReconnectToken">Opaque reconnect credential from the prior session.</param>
/// <param name="LastEpoch">Last known room state epoch.</param>
/// <param name="SessionStartTs">Wall-clock ms when the session started.</param>
/// <param name="ExpiresAtMs">Wall-clock ms when this record expires.</param>
public sealed record RecoveryRecord(
    string RoomId,
    string Cid,
    string ReconnectToken,
    long LastEpoch,
    long SessionStartTs,
    long ExpiresAtMs
);
