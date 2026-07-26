using Serenada.Core.Models;
using Serenada.Core.Networking;
using Serenada.Core.Signaling;

namespace Serenada.Core;

/// <summary>
/// Main entry point for the Serenada SDK on Windows.
///
/// Create an instance with a <see cref="SerenadaConfig"/>, then use <see cref="Join(string,string?,string?)"/>
/// to start a call session or <see cref="CreateRoomAsync"/> to create a new room.
///
/// Mirrors the cross-platform <c>SerenadaCore</c> class on Android, iOS, and Web.
/// </summary>
public class SerenadaCore
{
    /// <summary>SDK version. Must match across all platforms.</summary>
    public const string Version = "0.9.1";

    private readonly SerenadaConfig _config;
    private readonly CoreApiClient _apiClient;
    private readonly RecoveryStorage _recoveryStorage;

    /// <summary>Callback delegate for session lifecycle events.</summary>
    public ISerenadaCoreDelegate? Delegate { get; set; }

    /// <summary>Custom logger for SDK diagnostic output.</summary>
    public ISerenadaLogger? Logger { get; set; }

    /// <summary>
    /// Create a new SerenadaCore instance.
    /// </summary>
    /// <param name="config">SDK configuration. Must have exactly one of
    /// <see cref="SerenadaConfig.ServerHost"/> or <see cref="SerenadaConfig.SignalingProvider"/>.</param>
    public SerenadaCore(SerenadaConfig config)
    {
        _config = config;
        _config.Validate();
        _apiClient = new CoreApiClient();
        _recoveryStorage = new RecoveryStorage();
    }

    /// <summary>
    /// Returns a recoverable session if the previous process ended abruptly
    /// (kill, crash) while a call was active and the persisted reconnect token
    /// is still within its TTL.
    /// Host apps should call this on launch and surface a "Rejoin call?" prompt.
    /// Returns <c>null</c> when there is nothing to recover.
    /// </summary>
    public RecoveryRecord? GetRecoverableSession()
    {
        AssertMainThread();
        return _recoveryStorage.Load();
    }

    /// <summary>
    /// Drops any persisted recovery record. Call when the user explicitly
    /// declines the rejoin prompt.
    /// </summary>
    public void DiscardRecoverableSession()
    {
        AssertMainThread();
        _recoveryStorage.Clear();
    }

    /// <summary>
    /// Rejoin a session recovered after an unexpected process termination.
    /// Preserves the server-issued CID and reconnect credential.
    /// </summary>
    public SerenadaSession Rejoin(
        RecoveryRecord record,
        string? displayName = null,
        string? peerId = null)
    {
        AssertMainThread();
        if (record.ExpiresAtMs <= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
        {
            _recoveryStorage.Clear();
            throw new InvalidOperationException("The recoverable session has expired.");
        }

        var roomUrl = _config.ServerHost is { } host
            ? CoreApiClient.BuildRoomUrl(host, record.RoomId)
            : null;
        return JoinInternal(
            roomId: record.RoomId,
            roomUrl: roomUrl,
            displayName: displayName,
            peerId: peerId,
            reconnectCid: record.Cid,
            reconnectToken: record.ReconnectToken);
    }

    /// <summary>
    /// Join a call using a full URL (e.g. <c>"https://serenada.app/call/ABC123"</c>).
    /// </summary>
    /// <param name="url">Full call URL. The last path segment is the room ID.</param>
    /// <param name="displayName">Optional display name for this participant.</param>
    /// <param name="peerId">
    /// Optional host-supplied stable identity for this user (distinct from the
    /// per-call client ID). Surfaced on remote participants so the call UI can
    /// resolve avatars.
    /// </param>
    /// <returns>A started <see cref="SerenadaSession"/>.</returns>
    public SerenadaSession Join(string url, string? displayName = null, string? peerId = null)
    {
        AssertMainThread();

        var resolved = ResolveRoomUrl(url);

        return JoinInternal(
            roomId: resolved.RoomId,
            roomUrl: resolved.RoomUrl,
            displayName: displayName,
            peerId: peerId,
            reconnectCid: null,
            reconnectToken: null);
    }

    /// <summary>
    /// Join a call using a room ID.
    /// </summary>
    /// <param name="roomId">Room identifier.</param>
    /// <param name="serverHost">
    /// Server host override. If <c>null</c>, uses the configured <see cref="SerenadaConfig.ServerHost"/>.
    /// </param>
    /// <param name="displayName">Optional display name.</param>
    /// <param name="peerId">Optional host-supplied stable identity.</param>
    /// <returns>A started <see cref="SerenadaSession"/>.</returns>
    public SerenadaSession Join(string roomId, string? serverHost = null,
        string? displayName = null, string? peerId = null)
    {
        AssertMainThread();

        var host = serverHost ?? _config.ServerHost;
        var roomUrl = host is not null ? CoreApiClient.BuildRoomUrl(host, roomId) : null;

        return JoinInternal(
            roomId: roomId,
            roomUrl: roomUrl,
            displayName: displayName,
            peerId: peerId,
            reconnectCid: null,
            reconnectToken: null);
    }

    /// <summary>
    /// Create a new room. Returns the room URL and ID.
    /// Call <see cref="Join(string,string?,string?)"/> to start the call.
    /// </summary>
    public async Task<CreateRoomResult> CreateRoomAsync()
    {
        AssertMainThread();

        var serverHost = _config.ServerHost
            ?? throw new InvalidOperationException("ServerHost is required to create a room.");

        var roomId = await _apiClient.CreateRoomIdAsync(serverHost);
        var roomUrl = CoreApiClient.BuildRoomUrl(serverHost, roomId);

        return new CreateRoomResult(roomId, roomUrl);
    }

    // ── Internal join logic ───────────────────────────────────

    internal SerenadaSession JoinInternal(
        string roomId,
        string? roomUrl,
        string? displayName,
        string? peerId,
        string? reconnectCid,
        string? reconnectToken)
    {
        var signalingProvider = _config.SignalingProvider
            ?? CreateServerProvider();

        var session = new SerenadaSession(
            config: _config,
            roomId: roomId,
            roomUrl: roomUrl,
            signalingProvider: signalingProvider,
            displayName: displayName,
            peerId: peerId,
            reconnectCid: reconnectCid,
            reconnectToken: reconnectToken,
            @delegate: Delegate,
            logger: Logger ?? _config.Logger,
            recoveryStorage: _recoveryStorage);

        session.Start();

        return session;
    }

    private ISignalingProvider CreateServerProvider()
    {
        var serverHost = _config.ServerHost
            ?? throw new InvalidOperationException("ServerHost is required for built-in signaling.");

        return new SerenadaServerProvider(
            serverHost: serverHost,
            transports: _config.Transports,
            logger: Logger ?? _config.Logger);
    }

    // ── URL parsing ───────────────────────────────────────────

    private static ResolvedRoomUrl ResolveRoomUrl(string url)
    {
        var trimmed = url.Trim();

        // Try to parse as a URI
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            var path = uri.AbsolutePath.TrimEnd('/');
            var roomId = path.Split('/').LastOrDefault()
                ?? throw new ArgumentException("Cannot extract room ID from URL.");

            if (string.IsNullOrWhiteSpace(roomId))
                throw new ArgumentException("Cannot extract room ID from URL.");

            var authority = uri.Authority;
            var scheme = uri.Scheme;
            var roomUrl = $"{scheme}://{authority}/call/{roomId}";

            return new ResolvedRoomUrl(roomId, roomUrl);
        }

        // Fallback: treat as room-id-containing string
        var lastSegment = trimmed.Split('/').LastOrDefault();
        if (string.IsNullOrWhiteSpace(lastSegment))
            throw new ArgumentException("Cannot extract room ID from URL.");

        return new ResolvedRoomUrl(lastSegment, trimmed);
    }

    private static void AssertMainThread()
    {
        // On Windows, the main thread is identified by the DispatcherQueue.
        // In a console/test context, we skip this check.
        // Full enforcement will be added when the UI host is wired up.
    }

    private sealed record ResolvedRoomUrl(string RoomId, string RoomUrl);
}
