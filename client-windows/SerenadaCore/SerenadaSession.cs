using Serenada.Core.Call;
using Serenada.Core.Models;
using Serenada.Core.Signaling;

namespace Serenada.Core;

/// <summary>
/// Active call session. Owns the orchestration between signaling, media, stats,
/// and quality tracking. Creates one session per call.
///
/// All public methods must be called from the same thread that created the session.
/// Mirrors <c>SerenadaSession</c> on Android/iOS and <c>SerenadaSessionHandle</c> on Web.
/// </summary>
public class SerenadaSession : IDisposable
{
    private readonly SerenadaConfig _config;
    private readonly ISignalingProvider _signalingProvider;
    private readonly ISerenadaCoreDelegate? _delegate;
    private readonly ISerenadaLogger? _logger;
    private readonly RecoveryStorage _recoveryStorage;
    private readonly string _displayName;
    private readonly string? _peerId;
    private readonly string? _reconnectCid;
    private readonly string? _reconnectToken;

    // ── State ─────────────────────────────────────────────────

    private CallState _state = new();
    private CallDiagnostics _diagnostics = new();

    private readonly List<Action<CallState>> _stateListeners = [];
    private readonly List<Action<PeerMessage>> _peerMessageListeners = [];
    private readonly List<Action<ConnectionEvent>> _connectionEventListeners = [];

    // ── Internal engines (created during Start) ───────────────

    private SignalingMessageRouter? _messageRouter;
    private JoinFlowCoordinator? _joinFlowCoordinator;
    private MediaLivenessEmitter? _livenessEmitter;
    private ContentStateBroadcaster? _contentState;

    // ── Properties ────────────────────────────────────────────

    /// <summary>Current call state snapshot.</summary>
    public CallState State => _state;

    /// <summary>Current diagnostics snapshot.</summary>
    public CallDiagnostics Diagnostics => _diagnostics;

    /// <summary>Room identifier for this session.</summary>
    public string RoomId { get; }

    /// <summary>Full room URL, if available.</summary>
    public string? RoomUrl { get; }

    /// <summary>Whether independent content video is enabled for this session.</summary>
    public bool IndependentContentVideoEnabled => _config.EnableIndependentContentVideo;

    /// <summary>Aggregated call-quality summary. <c>null</c> before sampling begins.</summary>
    public CallQualitySummary? QualitySummary { get; private set; }

    /// <summary>Whether camera flipping is available (multiple cameras).</summary>
    public bool HasMultipleCameras { get; private set; }

    /// <summary>Whether screen share is available on this device.</summary>
    public bool CanScreenShare { get; private set; }

    /// <summary>Whether the signaling transport is currently connected.</summary>
    public bool IsSignalingConnected => _diagnostics.IsSignalingConnected;

    /// <summary>
    /// Callback invoked when permissions are required before joining.
    /// Set this before calling <see cref="Start"/> or <see cref="ResumeJoinAsync"/>.
    /// </summary>
    public Action<IReadOnlyList<MediaCapability>>? OnPermissionsRequired { get; set; }

    // ── Construction ──────────────────────────────────────────

    internal SerenadaSession(
        SerenadaConfig config,
        string roomId,
        string? roomUrl,
        ISignalingProvider signalingProvider,
        string? displayName,
        string? peerId,
        string? reconnectCid,
        string? reconnectToken,
        ISerenadaCoreDelegate? @delegate,
        ISerenadaLogger? logger,
        RecoveryStorage recoveryStorage)
    {
        _config = config;
        RoomId = roomId;
        RoomUrl = roomUrl;
        _signalingProvider = signalingProvider;
        _displayName = displayName ?? string.Empty;
        _peerId = peerId;
        _reconnectCid = reconnectCid;
        _reconnectToken = reconnectToken;
        _delegate = @delegate;
        _logger = logger;
        _recoveryStorage = recoveryStorage;
    }

    // ── Start / Lifecycle ─────────────────────────────────────

    /// <summary>
    /// Start the session. Connects the signaling provider and begins the join flow.
    /// Called automatically by <see cref="SerenadaCore.Join(string,string?,string?)"/>.
    /// </summary>
    internal void Start()
    {
        _state = _state with { Phase = CallPhase.Joining };
        CommitState();

        _messageRouter = new SignalingMessageRouter(
            getRoomId: () => RoomId,
            onJoined: HandleJoined,
            onRoomState: HandleRoomState,
            onRoomEnded: HandleRoomEnded,
            onError: HandleError,
            onPong: HandlePong,
            onTurnRefreshed: HandleTurnRefreshed,
            onSignalingPayload: HandleSignalingPayload,
            onContentState: HandleContentState,
            onNegotiationDirty: HandleNegotiationDirty,
            onRelayFailed: HandleRelayFailed,
            onReconnectTokenRefreshed: HandleReconnectTokenRefreshed,
            log: Log);

        _joinFlowCoordinator = new JoinFlowCoordinator(
            onRequestPermissions: HandlePermissionsRequired,
            onStartJoin: () =>
            {
                _signalingProvider.Connect();
            },
            onSendJoin: () =>
            {
                _signalingProvider.JoinRoom(RoomId, BuildJoinOptions());
            },
            onTimeout: () =>
            {
                FailJoin(new CallError(CallErrorCode.ConnectionFailed, "Join timed out."));
            },
            onEnsureConnection: () =>
            {
                if (!IsSignalingConnected)
                    _signalingProvider.Connect();
            },
            log: Log);

        // Wire signaling provider events
        _signalingProvider.OnConnected += HandleProviderConnected;
        _signalingProvider.OnDisconnected += HandleProviderDisconnected;
        _signalingProvider.OnJoined += HandleProviderJoined;
        _signalingProvider.OnRoomStateUpdated += HandleProviderRoomStateUpdated;
        _signalingProvider.OnPeerJoined += HandleProviderPeerJoined;
        _signalingProvider.OnPeerLeft += HandleProviderPeerLeft;
        _signalingProvider.OnMessage += HandleProviderMessage;
        _signalingProvider.OnRoomEnded += HandleProviderRoomEnded;
        _signalingProvider.OnError += HandleProviderError;
        _signalingProvider.OnIceServersChanged += HandleProviderIceServersChanged;
        _signalingProvider.OnNegotiationDirty += HandleProviderNegotiationDirty;
        _signalingProvider.OnRelayFailed += HandleProviderRelayFailed;
        _signalingProvider.OnReconnectTokenRefreshed += HandleProviderReconnectTokenRefreshed;

        // Create resilience emitters
        _livenessEmitter = new MediaLivenessEmitter(
            getActiveCids: () => _state.RemoteParticipants
                .Where(p => p.CameraReceiving || p.ContentReceiving)
                .Select(p => p.Cid)
                .ToList(),
            broadcast: (payload) => _signalingProvider.Broadcast("media_liveness", payload),
            logger: _logger);

        _contentState = new ContentStateBroadcaster(
            broadcast: (type, payload) => _signalingProvider.Broadcast(type, payload),
            logger: _logger);

        // Begin join flow
        _joinFlowCoordinator.Begin();
    }

    /// <summary>
    /// Resume join after permissions have been granted.
    /// </summary>
    public Task ResumeJoinAsync()
    {
        if (_state.Phase != CallPhase.AwaitingPermissions)
            return Task.CompletedTask;

        _state = _state with { Phase = CallPhase.Joining, RequiredPermissions = null };
        CommitState();
        _joinFlowCoordinator?.Begin();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Cancel the current join attempt.
    /// </summary>
    public void CancelJoin()
    {
        if (_state.Phase is CallPhase.Joining or CallPhase.AwaitingPermissions)
        {
            _joinFlowCoordinator?.Cancel();
            _signalingProvider.Disconnect();
            _state = _state with { Phase = CallPhase.Idle };
            CommitState();
        }
    }

    // ── Public Controls ───────────────────────────────────────

    /// <summary>Gracefully leave the room (other participants stay).</summary>
    public void Leave()
    {
        _signalingProvider.LeaveRoom();
        Teardown(new EndReason.LocalLeft());
    }

    /// <summary>End the room for everyone (host only).</summary>
    public void End()
    {
        _signalingProvider.EndRoom();
    }

    /// <summary>Toggle local audio on/off.</summary>
    public void ToggleAudio()
    {
        var enabled = !_state.LocalParticipant?.AudioEnabled ?? true;
        SetAudioEnabled(enabled);
    }

    /// <summary>Toggle local video on/off.</summary>
    public void ToggleVideo()
    {
        var enabled = !_state.LocalParticipant?.VideoEnabled ?? true;
        SetVideoEnabled(enabled);
    }

    /// <summary>Set audio explicitly.</summary>
    public void SetAudioEnabled(bool enabled)
    {
        UpdateLocalParticipant(p => p with { AudioEnabled = enabled });
        _signalingProvider.Broadcast(
            SignalingProtocolConstants.TypeParticipantMediaState,
            new { audioEnabled = enabled });
    }

    /// <summary>Set video explicitly.</summary>
    public void SetVideoEnabled(bool enabled)
    {
        UpdateLocalParticipant(p => p with
        {
            VideoEnabled = enabled,
            CameraEnabled = enabled,
        });
        _signalingProvider.Broadcast(
            SignalingProtocolConstants.TypeParticipantMediaState,
            new { videoEnabled = enabled });
    }

    /// <summary>Flip to the next available camera mode.</summary>
    public Task FlipCameraAsync()
    {
        var current = _state.LocalParticipant?.CameraMode ?? CameraMode.Selfie;
        var modes = _state.LocalParticipant?.AvailableCameraModes ?? [];
        var idx = FindIndex(modes, current);
        var next = modes.Count > 0 ? modes[(idx + 1) % modes.Count] : current;
        return SetCameraModeAsync(next);
    }

    /// <summary>Set a specific camera mode.</summary>
    public Task SetCameraModeAsync(CameraMode mode)
    {
        UpdateLocalParticipant(p => p with { CameraMode = mode });
        // TODO: Wire to camera capture controller in Phase 3
        return Task.CompletedTask;
    }

    /// <summary>Start screen sharing.</summary>
    public Task StartScreenShareAsync()
    {
        _contentState?.StartSharing();
        UpdateLocalParticipant(p => p with
        {
            Content = new ParticipantContent
            {
                Active = true,
                Type = SignalingProtocolConstants.ContentTypeScreenShare,
                Revision = 1,
            },
        });
        return Task.CompletedTask;
    }

    /// <summary>Stop screen sharing.</summary>
    public Task StopScreenShareAsync()
    {
        _contentState?.StopSharing();
        UpdateLocalParticipant(p => p with { Content = null });
        return Task.CompletedTask;
    }

    // ── State Observation ─────────────────────────────────────

    /// <summary>
    /// Subscribe to state changes. Returns an unsubscribe action.
    /// Mirrors the Web <c>subscribe()</c> pattern.
    /// </summary>
    public Action Subscribe(Action<CallState> listener)
    {
        _stateListeners.Add(listener);
        // Immediately deliver current state
        listener(_state);
        return () => _stateListeners.Remove(listener);
    }

    /// <summary>
    /// Subscribe to peer messages. Returns an unsubscribe action.
    /// </summary>
    public Action OnPeerMessage(Action<PeerMessage> listener)
    {
        _peerMessageListeners.Add(listener);
        return () => _peerMessageListeners.Remove(listener);
    }

    /// <summary>
    /// Subscribe to connection-quality events.
    /// </summary>
    public Action OnConnectionEvent(Action<ConnectionEvent> listener)
    {
        _connectionEventListeners.Add(listener);
        return () => _connectionEventListeners.Remove(listener);
    }

    // ── Cleanup ───────────────────────────────────────────────

    /// <summary>
    /// Permanently destroy the session. Stops all media, closes signaling,
    /// and clears state. The session cannot be used after this.
    /// </summary>
    public void Dispose()
    {
        _joinFlowCoordinator?.Cancel();
        _signalingProvider.Disconnect();

        // Unwire events
        _signalingProvider.OnConnected -= HandleProviderConnected;
        _signalingProvider.OnDisconnected -= HandleProviderDisconnected;
        _signalingProvider.OnJoined -= HandleProviderJoined;
        _signalingProvider.OnRoomStateUpdated -= HandleProviderRoomStateUpdated;
        _signalingProvider.OnPeerJoined -= HandleProviderPeerJoined;
        _signalingProvider.OnPeerLeft -= HandleProviderPeerLeft;
        _signalingProvider.OnMessage -= HandleProviderMessage;
        _signalingProvider.OnRoomEnded -= HandleProviderRoomEnded;
        _signalingProvider.OnError -= HandleProviderError;
        _signalingProvider.OnIceServersChanged -= HandleProviderIceServersChanged;
        _signalingProvider.OnNegotiationDirty -= HandleProviderNegotiationDirty;
        _signalingProvider.OnRelayFailed -= HandleProviderRelayFailed;
        _signalingProvider.OnReconnectTokenRefreshed -= HandleProviderReconnectTokenRefreshed;

        _livenessEmitter?.Dispose();
        _livenessEmitter = null;

        _state = new CallState();
        _diagnostics = new CallDiagnostics();
        _stateListeners.Clear();
        _peerMessageListeners.Clear();
        _connectionEventListeners.Clear();
    }

    // ── Internal state mutation ───────────────────────────────

    private void CommitState()
    {
        foreach (var listener in _stateListeners)
        {
            try { listener(_state); }
            catch { /* Don't let listener exceptions break the SDK */ }
        }
        _delegate?.OnSessionStateChanged(this, _state);
    }

    private void UpdateLocalParticipant(Func<LocalParticipant, LocalParticipant> update)
    {
        if (_state.LocalParticipant is { } lp)
        {
            _state = _state with { LocalParticipant = update(lp) };
            CommitState();
        }
    }

    private void UpdateDiagnostics(Func<CallDiagnostics, CallDiagnostics> update)
    {
        _diagnostics = update(_diagnostics);
    }

    private void FailJoin(CallError error)
    {
        _joinFlowCoordinator?.Cancel();
        _state = _state with { Phase = CallPhase.Error, Error = error };
        CommitState();
    }

    private void Teardown(EndReason reason)
    {
        _state = _state with { Phase = CallPhase.Ending };
        CommitState();

        _delegate?.OnSessionEnded(this, reason);

        _state = _state with { Phase = CallPhase.Idle };
        CommitState();
    }

    private JoinOptions BuildJoinOptions()
    {
        return new JoinOptions
        {
            Device = "desktop",
            DisplayName = _displayName,
            PeerId = _peerId,
            ReconnectCid = _reconnectCid,
            ReconnectToken = _reconnectToken,
            IndependentContentVideo = _config.EnableIndependentContentVideo,
            VideoMediaEnabled = _config.VideoMediaEnabled,
            MaxParticipants = 4,
            CreateMaxParticipants = 2,
        };
    }

    // ── Signaling Provider Event Handlers ─────────────────────

    private void HandleProviderConnected(ConnectionInfo info)
    {
        Log(SerenadaLogLevel.Info, "Session", $"Signaling connected via {info.Transport}.");
        UpdateDiagnostics(d => d with { IsSignalingConnected = true, ActiveTransport = info.Transport });
        _joinFlowCoordinator?.OnConnected();

        // Send join now if not already sent
        _signalingProvider.JoinRoom(RoomId, BuildJoinOptions());
    }

    private void HandleProviderDisconnected(string? reason)
    {
        Log(SerenadaLogLevel.Warning, "Session", $"Signaling disconnected: {reason}.");
        UpdateDiagnostics(d => d with { IsSignalingConnected = false, ActiveTransport = null });
        _joinFlowCoordinator?.OnDisconnected();
    }

    private void HandleProviderJoined(JoinedPayload payload) => _messageRouter?.ProcessJoined(payload);
    private void HandleProviderRoomStateUpdated(RoomStatePayload payload) => _messageRouter?.ProcessRoomState(payload);
    private void HandleProviderPeerJoined(SignalingParticipant p) => _messageRouter?.ProcessPeerJoined(p);
    private void HandleProviderPeerLeft(SignalingParticipant p) => _messageRouter?.ProcessPeerLeft(p);
    private void HandleProviderMessage(PeerMessage msg) => _messageRouter?.ProcessPeerMessage(msg);
    private void HandleProviderRoomEnded(string by) => _messageRouter?.ProcessRoomEnded(by);
    private void HandleProviderError(ErrorPayload error) => _messageRouter?.ProcessError(error);
    private void HandleProviderIceServersChanged(IReadOnlyList<IceServer> servers) { /* TODO: Phase 3 */ }
    private void HandleProviderNegotiationDirty(NegotiationDirtyPayload p) => _messageRouter?.ProcessNegotiationDirty(p);
    private void HandleProviderRelayFailed(RelayFailedPayload p) => _messageRouter?.ProcessRelayFailed(p);
    private void HandleProviderReconnectTokenRefreshed(ReconnectTokenRefreshedPayload p) => _messageRouter?.ProcessReconnectTokenRefreshed(p);

    // ── Message Router Callbacks ──────────────────────────────

    private void HandleJoined(JoinedPayload payload)
    {
        _state = _state with
        {
            Phase = CallPhase.Waiting,
            LocalParticipant = new LocalParticipant
            {
                Cid = null, // Set after we parse our own participant
                DisplayName = _displayName,
                PeerId = _peerId,
                AudioEnabled = _config.DefaultAudioEnabled,
                VideoEnabled = _config.DefaultVideoEnabled,
                CameraEnabled = _config.DefaultVideoEnabled,
            },
            RemoteParticipants = [],
            CallStartedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };
        CommitState();
        _joinFlowCoordinator?.OnJoinAcknowledged();
    }

    private void HandleRoomState(RoomStatePayload payload)
    {
        // Update remote participants
        var remoteParticipants = payload.Participants
            .Select(MapToRemoteParticipant)
            .ToList()
            .AsReadOnly();

        var newPhase = payload.Participants.Count > 1 ? CallPhase.InCall : CallPhase.Waiting;

        _state = _state with
        {
            RemoteParticipants = remoteParticipants,
            Phase = newPhase,
        };
        CommitState();

        // Start/stop media liveness based on call phase
        if (newPhase == CallPhase.InCall)
            _livenessEmitter?.Start();
        else if (newPhase == CallPhase.Waiting)
            _livenessEmitter?.Stop();
    }

    private void HandleRoomEnded(string by)
    {
        Log(SerenadaLogLevel.Info, "Session", $"Room ended by {by}.");
        _livenessEmitter?.Stop();
        Teardown(new EndReason.RemoteEnded());
    }

    private void HandleError(ErrorPayload error)
    {
        Log(SerenadaLogLevel.Error, "Session", $"Signaling error: {error.Code} — {error.Message}");
        var callError = MapErrorCode(error.Code, error.Message ?? error.Code);
        _state = _state with { Phase = CallPhase.Error, Error = callError };
        CommitState();
    }

    private void HandlePong() { /* Keepalive — no action needed */ }

    private void HandleTurnRefreshed(TurnRefreshedPayload payload) { /* TODO: Phase 3 */ }

    private void HandleSignalingPayload(PeerMessage message)
    {
        // Forward peer messages to listeners
        foreach (var listener in _peerMessageListeners)
        {
            try { listener(message); }
            catch { /* Don't let listener exceptions break the SDK */ }
        }
    }

    private void HandleContentState(ContentStatePayload payload)
    {
        if (payload.FromCid == null) return;

        _state = _state with
        {
            RemoteParticipants = _state.RemoteParticipants
                .Select(p =>
                {
                    if (p.Cid != payload.FromCid) return p;

                    if (!payload.Active)
                        return p with { Content = null };

                    // Track highest revision per CID (ordering rule from protocol spec)
                    if (payload.Revision is { } rev
                        && p.Content?.Revision >= rev)
                        return p; // Stale — ignore

                    return p with
                    {
                        Content = new ParticipantContent
                        {
                            Active = payload.Active,
                            Type = payload.ContentType
                                ?? SignalingProtocolConstants.ContentTypeScreenShare,
                            Revision = payload.Revision ?? 0,
                        },
                    };
                })
                .ToList()
                .AsReadOnly(),
        };
        CommitState();
    }

    private void HandleNegotiationDirty(NegotiationDirtyPayload payload)
    {
        // Peer reattached — schedule fresh negotiation for this CID.
        // Full implementation: trigger ICE restart or new offer for the dirty peer.
        Log(SerenadaLogLevel.Debug, "Session",
            $"Negotiation dirty with {payload.With} — renegotiation needed.");
    }

    private void HandleRelayFailed(RelayFailedPayload payload)
    {
        // Suppress further negotiation toward suspended peers.
        Log(SerenadaLogLevel.Debug, "Session",
            $"Relay failed to {string.Join(",", payload.Targets)} ({payload.Reason}).");
    }
    private void HandleReconnectTokenRefreshed(ReconnectTokenRefreshedPayload payload)
    {
        // Persist updated recovery token
        _recoveryStorage.Save(new RecoveryRecord(
            RoomId: RoomId,
            Cid: _state.LocalParticipant?.Cid ?? string.Empty,
            ReconnectToken: payload.ReconnectToken,
            LastEpoch: 0,
            SessionStartTs: _state.CallStartedAtMs ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            ExpiresAtMs: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                + (payload.ReconnectTokenTtlMs ?? WebRtcResilienceConstants.ReconnectTokenTtlFallbackMs)));
    }

    private void HandlePermissionsRequired(IReadOnlyList<MediaCapability> permissions)
    {
        _state = _state with { Phase = CallPhase.AwaitingPermissions, RequiredPermissions = permissions };
        CommitState();
        OnPermissionsRequired?.Invoke(permissions);
        _delegate?.OnPermissionsRequired(this, permissions);
    }

    // ── Helpers ───────────────────────────────────────────────

    private static RemoteParticipant MapToRemoteParticipant(SignalingParticipant sp)
    {
        return new RemoteParticipant
        {
            Cid = sp.Cid,
            DisplayName = sp.DisplayName,
            PeerId = sp.PeerId,
            AudioEnabled = sp.AudioEnabled,
            VideoEnabled = sp.VideoEnabled,
            CameraEnabled = sp.VideoEnabled,
            SignalingStatus = sp.ConnectionStatus switch
            {
                SignalingProtocolConstants.ConnectionStatusSuspended => ParticipantSignalingStatus.Suspended,
                _ => ParticipantSignalingStatus.Active,
            },
            SupportsIndependentContentVideo = sp.Capabilities?.IndependentContentVideo ?? false,
            Content = sp.ContentState is { Active: true } cs
                ? new ParticipantContent
                {
                    Active = cs.Active,
                    Type = cs.ContentType ?? SignalingProtocolConstants.ContentTypeScreenShare,
                    Revision = cs.Revision ?? 0,
                }
                : null,
        };
    }

    private static CallError MapErrorCode(string code, string message)
    {
        return code switch
        {
            SignalingProtocolConstants.ErrorRoomFull => new CallError(CallErrorCode.RoomFull, message),
            SignalingProtocolConstants.ErrorRoomCapacityUnsupported => new CallError(CallErrorCode.RoomFull, message),
            SignalingProtocolConstants.ErrorRoomEnded => new CallError(CallErrorCode.RoomEnded, message),
            SignalingProtocolConstants.ErrorInvalidReconnectToken => new CallError(CallErrorCode.SessionExpired, message),
            SignalingProtocolConstants.ErrorInternal => new CallError(CallErrorCode.ServerError, message),
            _ => new CallError(CallErrorCode.ServerError, message),
        };
    }

    private static int FindIndex(IReadOnlyList<CameraMode> list, CameraMode item)
    {
        for (var i = 0; i < list.Count; i++)
            if (list[i] == item) return i;
        return -1;
    }

    private void Log(SerenadaLogLevel level, string tag, string message)
    {
        _logger?.Log(level, tag, message);
    }
}
