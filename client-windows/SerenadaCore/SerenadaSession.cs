using Serenada.Core.Call;
using Serenada.Core.Models;
using Serenada.Core.Signaling;
using Serenada.Core.WebRtc;

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
    private readonly SynchronizationContext? _sessionContext;

    // ── State ─────────────────────────────────────────────────

    private CallState _state = new();
    private CallDiagnostics _diagnostics = new();

    private readonly List<Action<CallState>> _stateListeners = [];
    private readonly List<Action<PeerMessage>> _peerMessageListeners = [];
    private readonly List<Action<ConnectionEvent>> _connectionEventListeners = [];
    private readonly List<Action<IRtcVideoTrack?>> _localVideoTrackListeners = [];
    private readonly List<Action<string, IRtcVideoTrack?>> _remoteVideoTrackListeners = [];
    private readonly Dictionary<string, IRtcVideoTrack> _remoteVideoTracks = [];

    // ── Internal engines (created during Start) ───────────────

    private SignalingMessageRouter? _messageRouter;
    private JoinFlowCoordinator? _joinFlowCoordinator;
    private MediaLivenessEmitter? _livenessEmitter;
    private ContentStateBroadcaster? _contentState;
    private ISessionMediaEngine? _mediaEngine;
    private PeerNegotiationEngine? _negotiationEngine;
    private RoomStatePayload? _currentRoomState;
    private string? _localCid;
    private bool _mediaReady;
    private Task? _mediaInitializationTask;
    private bool _disposed;

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
    public bool IndependentContentVideoEnabled =>
        _mediaEngine?.SupportsIndependentContentVideo == true;

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
        _logger = logger ?? config.Logger;
        _recoveryStorage = recoveryStorage;
        _sessionContext = SynchronizationContext.Current;
    }

    // ── Start / Lifecycle ─────────────────────────────────────

    /// <summary>
    /// Start the session. Connects the signaling provider and begins the join flow.
    /// Called automatically by <see cref="SerenadaCore.Join(string,string?,string?)"/>.
    /// </summary>
    internal void Start()
    {
        _state = _state with
        {
            Phase = CallPhase.Joining,
            RoomId = RoomId,
            RoomUrl = RoomUrl,
            ConnectionStatus = ConnectionStatus.Recovering,
            SignalingState = new SignalingState.Reconnecting(0, null),
        };
        CommitState();

        _mediaEngine = new WebRtcEngine(_config, _logger);
        _mediaEngine.LocalVideoTrackChanged += HandleLocalVideoTrackChanged;
        _negotiationEngine = new PeerNegotiationEngine(
            mediaEngine: _mediaEngine,
            getLocalCid: () => _localCid,
            getRoomState: () => _currentRoomState,
            isSignalingConnected: () => IsSignalingConnected,
            deferInitialAnswer: () => _config.DeferInitialAnswer,
            sendToPeer: (cid, type, payload) =>
                _signalingProvider.SendToPeer(cid, type, payload),
            onRemoteVideoTrackAdded: HandleRemoteVideoTrackAdded,
            onRemoteVideoTrackRemoved: HandleRemoteVideoTrackRemoved,
            onPeerConnectionChanged: HandlePeerConnectionChanged,
            dispatch: Dispatch,
            logger: _logger);

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
            onTimeout: () => Dispatch(() =>
                FailJoin(new CallError(CallErrorCode.ConnectionFailed, "Join timed out."))),
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
        _recoveryStorage.Clear();
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
        var effectiveEnabled = enabled &&
            (!_mediaReady || _mediaEngine?.LocalAudioSource != null);
        _mediaEngine?.SetAudioEnabled(effectiveEnabled);
        UpdateLocalParticipant(p => p with { AudioEnabled = effectiveEnabled });
        BroadcastLocalMediaState();
    }

    /// <summary>Set video explicitly.</summary>
    public void SetVideoEnabled(bool enabled)
    {
        var effectiveEnabled = enabled &&
            (!_mediaReady || _mediaEngine?.LocalVideoTrack != null);
        _mediaEngine?.SetVideoEnabled(effectiveEnabled);
        UpdateLocalParticipant(p => p with
        {
            VideoEnabled = effectiveEnabled,
            CameraEnabled = effectiveEnabled,
        });
        BroadcastLocalMediaState();
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
    public async Task SetCameraModeAsync(CameraMode mode)
    {
        if (_mediaEngine == null ||
            !_mediaEngine.AvailableCameraModes.Contains(mode))
        {
            return;
        }

        await _mediaEngine.SetCameraModeAsync(mode);
        UpdateLocalParticipant(p => p with
        {
            CameraMode = _mediaEngine.CurrentCameraMode,
        });
    }

    /// <summary>Start screen sharing.</summary>
    public async Task StartScreenShareAsync()
    {
        if (_mediaEngine == null || !_mediaEngine.CanScreenShare)
            return;

        await _mediaEngine.StartScreenShareAsync();
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
    }

    /// <summary>Stop screen sharing.</summary>
    public async Task StopScreenShareAsync()
    {
        if (_mediaEngine == null)
            return;

        await _mediaEngine.StopScreenShareAsync();
        _contentState?.StopSharing();
        UpdateLocalParticipant(p => p with { Content = null });
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
    /// Subscribe to local camera track changes for native preview rendering.
    /// The current track is delivered immediately.
    /// </summary>
    public Action SubscribeLocalVideoTrack(Action<IRtcVideoTrack?> listener)
    {
        _localVideoTrackListeners.Add(listener);
        listener(_mediaEngine?.LocalVideoTrack);
        return () => _localVideoTrackListeners.Remove(listener);
    }

    /// <summary>
    /// Subscribe to remote camera track changes. A <c>null</c> track removes
    /// the renderer for that CID.
    /// </summary>
    public Action SubscribeRemoteVideoTrack(
        Action<string, IRtcVideoTrack?> listener)
    {
        _remoteVideoTrackListeners.Add(listener);
        foreach (var (cid, track) in _remoteVideoTracks)
        {
            listener(cid, track);
        }
        return () => _remoteVideoTrackListeners.Remove(listener);
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
        if (_disposed) return;
        _disposed = true;

        _joinFlowCoordinator?.Cancel();

        // Explicitly leave before closing the transport. This prevents a
        // stale participant from remaining in the previous room while the
        // host application creates a session for another room.
        _signalingProvider.LeaveRoom();
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
        _contentState = null;
        _negotiationEngine?.Dispose();
        _negotiationEngine = null;
        if (_mediaEngine != null)
        {
            _mediaEngine.LocalVideoTrackChanged -= HandleLocalVideoTrackChanged;
            _mediaEngine.Dispose();
            _mediaEngine = null;
        }
        _remoteVideoTracks.Clear();
        _recoveryStorage.Clear();

        _state = new CallState();
        _diagnostics = new CallDiagnostics();
        _stateListeners.Clear();
        _peerMessageListeners.Clear();
        _connectionEventListeners.Clear();
        _localVideoTrackListeners.Clear();
        _remoteVideoTrackListeners.Clear();
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

    private void BroadcastLocalMediaState()
    {
        if (_state.LocalParticipant is not { } local)
            return;

        _signalingProvider.Broadcast(
            SignalingProtocolConstants.TypeParticipantMediaState,
            new
            {
                audioEnabled = local.AudioEnabled,
                videoEnabled = local.VideoEnabled,
            });
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

        _recoveryStorage.Clear();
        _livenessEmitter?.Stop();
        _signalingProvider.Disconnect();
        _negotiationEngine?.Dispose();
        _negotiationEngine = null;
        _mediaEngine?.Release();
        _remoteVideoTracks.Clear();

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
            IndependentContentVideo =
                _mediaEngine?.SupportsIndependentContentVideo == true,
            VideoMediaEnabled = _config.VideoMediaEnabled,
            MaxParticipants = 4,
            CreateMaxParticipants = 4,
        };
    }

    // ── Signaling Provider Event Handlers ─────────────────────

    private void HandleProviderConnected(ConnectionInfo info) =>
        Dispatch(() => ProcessProviderConnected(info));

    private void ProcessProviderConnected(ConnectionInfo info)
    {
        Log(SerenadaLogLevel.Info, "Session", $"Signaling connected via {info.Transport}.");
        UpdateDiagnostics(d => d with { IsSignalingConnected = true, ActiveTransport = info.Transport });
        _state = _state with
        {
            ConnectionStatus = ConnectionStatus.Connected,
            SignalingState = new SignalingState.Connected(),
            ActiveTransport = info.Transport,
        };
        CommitState();
        _joinFlowCoordinator?.OnConnected();
    }

    private void HandleProviderDisconnected(string? reason) =>
        Dispatch(() => ProcessProviderDisconnected(reason));

    private void ProcessProviderDisconnected(string? reason)
    {
        Log(SerenadaLogLevel.Warning, "Session", $"Signaling disconnected: {reason}.");
        UpdateDiagnostics(d => d with { IsSignalingConnected = false, ActiveTransport = null });
        if (_state.Phase is CallPhase.Ending or CallPhase.Idle)
            return;

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _state = _state with
        {
            ConnectionStatus = ConnectionStatus.Recovering,
            SignalingState = new SignalingState.Suspended(
                now,
                now + WebRtcResilienceConstants.SuspendHardEvictionTimeoutMs),
            ActiveTransport = null,
        };
        CommitState();
        _joinFlowCoordinator?.OnDisconnected();
    }

    private void HandleProviderJoined(JoinedPayload payload) =>
        Dispatch(() => _messageRouter?.ProcessJoined(payload));
    private void HandleProviderRoomStateUpdated(RoomStatePayload payload) =>
        Dispatch(() => _messageRouter?.ProcessRoomState(payload));
    private void HandleProviderPeerJoined(SignalingParticipant p) =>
        Dispatch(() => _messageRouter?.ProcessPeerJoined(p));
    private void HandleProviderPeerLeft(SignalingParticipant p) =>
        Dispatch(() => _messageRouter?.ProcessPeerLeft(p));
    private void HandleProviderMessage(PeerMessage msg) =>
        Dispatch(() => _messageRouter?.ProcessPeerMessage(msg));
    private void HandleProviderRoomEnded(string by) =>
        Dispatch(() => _messageRouter?.ProcessRoomEnded(by));
    private void HandleProviderError(ErrorPayload error) =>
        Dispatch(() => _messageRouter?.ProcessError(error));
    private void HandleProviderIceServersChanged(IReadOnlyList<IceServer> servers) =>
        Dispatch(() => _mediaEngine?.SetIceServers(servers));
    private void HandleProviderNegotiationDirty(NegotiationDirtyPayload p) =>
        Dispatch(() => _messageRouter?.ProcessNegotiationDirty(p));
    private void HandleProviderRelayFailed(RelayFailedPayload p) =>
        Dispatch(() => _messageRouter?.ProcessRelayFailed(p));
    private void HandleProviderReconnectTokenRefreshed(ReconnectTokenRefreshedPayload p) =>
        Dispatch(() => _messageRouter?.ProcessReconnectTokenRefreshed(p));

    // ── Message Router Callbacks ──────────────────────────────

    private void HandleJoined(JoinedPayload payload)
    {
        _localCid = payload.LocalCid
            ?? _reconnectCid
            ?? (payload.Participants.Count == 1
                ? payload.Participants[0].Cid
                : null);
        _currentRoomState = new RoomStatePayload
        {
            HostCid = payload.HostCid,
            Participants = payload.Participants,
            MaxParticipants = payload.MaxParticipants,
            Epoch = payload.Epoch,
        };

        var localSignalingParticipant = payload.Participants
            .FirstOrDefault(p => p.Cid == _localCid);
        var remoteParticipants = payload.Participants
            .Where(p => p.Cid != _localCid)
            .Select(MapToRemoteParticipant)
            .ToList()
            .AsReadOnly();

        _state = _state with
        {
            Phase = remoteParticipants.Count > 0 ? CallPhase.InCall : CallPhase.Waiting,
            RoomId = RoomId,
            RoomUrl = RoomUrl,
            LocalParticipant = new LocalParticipant
            {
                Cid = _localCid,
                DisplayName = localSignalingParticipant?.DisplayName ?? _displayName,
                PeerId = localSignalingParticipant?.PeerId ?? _peerId,
                AudioEnabled = localSignalingParticipant?.AudioEnabled
                    ?? _config.DefaultAudioEnabled,
                VideoEnabled = localSignalingParticipant?.VideoEnabled
                    ?? _config.DefaultVideoEnabled,
                CameraEnabled = localSignalingParticipant?.VideoEnabled
                    ?? _config.DefaultVideoEnabled,
                IsHost = _localCid == payload.HostCid,
                AvailableCameraModes = [],
            },
            RemoteParticipants = remoteParticipants,
            ParticipantCount = payload.Participants.Count,
            ConnectionStatus = ConnectionStatus.Connected,
            SignalingState = new SignalingState.Connected(),
            CallStartedAtMs = _state.CallStartedAtMs
                ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Error = null,
        };
        CommitState();
        _joinFlowCoordinator?.OnJoinAcknowledged();

        if (!string.IsNullOrWhiteSpace(payload.ReconnectToken) &&
            !string.IsNullOrWhiteSpace(_localCid))
        {
            SaveRecoveryRecord(
                payload.ReconnectToken!,
                payload.ReconnectTokenTtlMs,
                payload.Epoch);
        }

        _mediaInitializationTask ??= EnsureMediaReadyAsync();
    }

    private void HandleRoomState(RoomStatePayload payload)
    {
        _currentRoomState = payload;
        var localParticipant = payload.Participants
            .FirstOrDefault(p => p.Cid == _localCid);
        var remoteParticipants = payload.Participants
            .Where(p => p.Cid != _localCid)
            .Select(MapToRemoteParticipant)
            .Select(MergeRemoteRuntimeState)
            .ToList()
            .AsReadOnly();

        var activeRemoteCids = remoteParticipants.Select(p => p.Cid).ToHashSet();
        foreach (var staleCid in _remoteVideoTracks.Keys
                     .Where(cid => !activeRemoteCids.Contains(cid))
                     .ToList())
        {
            _remoteVideoTracks.Remove(staleCid);
            NotifyRemoteVideoTrack(staleCid, null);
        }

        var newPhase = remoteParticipants.Count > 0
            ? CallPhase.InCall
            : CallPhase.Waiting;

        _state = _state with
        {
            RemoteParticipants = remoteParticipants,
            Phase = newPhase,
            ParticipantCount = payload.Participants.Count,
            LocalParticipant = _state.LocalParticipant is { } currentLocal
                ? currentLocal with
                {
                    Cid = _localCid,
                    DisplayName = localParticipant?.DisplayName
                        ?? currentLocal.DisplayName,
                    PeerId = localParticipant?.PeerId ?? currentLocal.PeerId,
                    IsHost = _localCid == payload.HostCid,
                }
                : null,
        };
        CommitState();

        if (_mediaReady)
            _negotiationEngine?.SyncPeers(payload);

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
        _recoveryStorage.Clear();
        Teardown(new EndReason.RemoteEnded());
    }

    private void HandleError(ErrorPayload error)
    {
        Log(SerenadaLogLevel.Error, "Session", $"Signaling error: {error.Code} — {error.Message}");
        var callError = MapErrorCode(error.Code, error.Message ?? error.Code);
        _state = _state with
        {
            Phase = CallPhase.Error,
            Error = callError,
            ConnectionStatus = ConnectionStatus.Disconnected,
            SignalingState = new SignalingState.Failed(callError),
        };
        if (error.Code is SignalingProtocolConstants.ErrorRoomEnded
            or SignalingProtocolConstants.ErrorInvalidReconnectToken)
        {
            _recoveryStorage.Clear();
        }
        CommitState();
    }

    private void HandlePong() { /* Keepalive — no action needed */ }

    private void HandleTurnRefreshed(TurnRefreshedPayload payload)
    {
        // The built-in provider installs the refreshed token, fetches fresh
        // credentials, and raises OnIceServersChanged. Custom providers own the
        // same operation, so the session has no additional payload work here.
    }

    private void HandleSignalingPayload(PeerMessage message)
    {
        if (message.Type == SignalingProtocolConstants.TypeParticipantMediaState)
        {
            var mediaState = SignalingPayloadParsers.ParseMediaState(message.Payload);
            var fromCid = mediaState?.FromCid ?? message.From;
            if (!string.IsNullOrWhiteSpace(fromCid) && mediaState != null)
            {
                _state = _state with
                {
                    RemoteParticipants = _state.RemoteParticipants
                        .Select(p => p.Cid == fromCid
                            ? p with
                            {
                                AudioEnabled = mediaState.AudioEnabled,
                                VideoEnabled = mediaState.VideoEnabled,
                                CameraEnabled = mediaState.VideoEnabled,
                            }
                            : p)
                        .ToList()
                        .AsReadOnly(),
                };
                CommitState();
            }
        }
        else
        {
            _negotiationEngine?.ProcessSignalingPayload(message);
        }

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
        RestartDirtyNegotiation(payload);
        // Peer reattached — schedule fresh negotiation for this CID.
        // Full implementation: trigger ICE restart or new offer for the dirty peer.
        Log(SerenadaLogLevel.Debug, "Session",
            $"Negotiation dirty with {payload.With} — renegotiation needed.");
    }

    private void RestartDirtyNegotiation(NegotiationDirtyPayload payload)
    {
        _negotiationEngine?.ScheduleDirtyPairRestart(payload.With);
    }

    private void HandleRelayFailed(RelayFailedPayload payload)
    {
        // Suppress further negotiation toward suspended peers.
        Log(SerenadaLogLevel.Debug, "Session",
            $"Relay failed to {string.Join(",", payload.Targets)} ({payload.Reason}).");
    }
    private void HandleReconnectTokenRefreshed(ReconnectTokenRefreshedPayload payload)
    {
        SaveRecoveryRecord(
            payload.ReconnectToken,
            payload.ReconnectTokenTtlMs,
            _currentRoomState?.Epoch);
    }

    private void HandlePermissionsRequired(IReadOnlyList<MediaCapability> permissions)
    {
        _state = _state with { Phase = CallPhase.AwaitingPermissions, RequiredPermissions = permissions };
        CommitState();
        OnPermissionsRequired?.Invoke(permissions);
        _delegate?.OnPermissionsRequired(this, permissions);
    }

    // ── Helpers ───────────────────────────────────────────────

    private async Task EnsureMediaReadyAsync()
    {
        if (_mediaEngine == null || _mediaReady || _disposed)
            return;

        try
        {
            await _mediaEngine.StartLocalMediaAsync(_config.VideoMediaEnabled);
            var iceServers = await FetchIceServersWithRetryAsync();
            if (_disposed || _mediaEngine == null)
                return;

            _mediaEngine.SetIceServers(iceServers);
            var audioReady = _mediaEngine.LocalAudioSource != null;
            var videoReady = _mediaEngine.LocalVideoTrack != null;
            var audioEnabled = (_state.LocalParticipant?.AudioEnabled
                ?? _config.DefaultAudioEnabled) && audioReady;
            var videoEnabled = (_state.LocalParticipant?.VideoEnabled
                ?? _config.DefaultVideoEnabled) && videoReady;
            _mediaEngine.SetAudioEnabled(audioEnabled);
            _mediaEngine.SetVideoEnabled(videoEnabled);
            _mediaReady = true;

            HasMultipleCameras = videoReady && _mediaEngine.HasMultipleCameras;
            CanScreenShare = _mediaEngine.CanScreenShare;
            var availableModes = _mediaEngine.AvailableCameraModes;
            var hasCamera = videoReady && availableModes.Count > 0;

            UpdateLocalParticipant(p => p with
            {
                AudioEnabled = audioEnabled,
                VideoEnabled = videoEnabled,
                CameraEnabled = videoEnabled,
                AvailableCameraModes = availableModes,
                CameraMode = hasCamera
                    ? _mediaEngine.CurrentCameraMode
                    : p.CameraMode,
            });

            BroadcastLocalMediaState();

            if (_currentRoomState != null)
                _negotiationEngine?.SyncPeers(_currentRoomState);
        }
        catch (Exception ex)
        {
            Log(SerenadaLogLevel.Error, "Session",
                $"Media initialization failed: {ex.Message}");
            FailJoin(new CallError(
                CallErrorCode.MediaUnavailable,
                "Camera or microphone initialization failed."));
        }
    }

    private void HandleLocalVideoTrackChanged(IRtcVideoTrack? track)
    {
        Dispatch(() =>
        {
            foreach (var listener in _localVideoTrackListeners.ToArray())
            {
                try { listener(track); }
                catch { /* Renderer failures must not break the SDK. */ }
            }
        });
    }

    private async Task<IReadOnlyList<IceServer>> FetchIceServersWithRetryAsync()
    {
        foreach (var delayMs in WebRtcResilienceConstants.IceFetchRetryDelaysMs)
        {
            if (_disposed)
                return [];
            if (delayMs > 0)
                await Task.Delay(delayMs);

            try
            {
                var servers = await _signalingProvider.GetIceServersAsync();
                if (servers.Count > 0)
                    return servers;
            }
            catch (Exception ex)
            {
                Log(SerenadaLogLevel.Warning, "Session",
                    $"ICE server fetch failed: {ex.Message}");
            }
        }

        Log(SerenadaLogLevel.Warning, "Session",
            "TURN credentials are unavailable; falling back to the default STUN server.");
        return
        [
            new IceServer
            {
                Urls = ["stun:stun.l.google.com:19302"],
            },
        ];
    }

    private void HandleRemoteVideoTrackAdded(string cid, IRtcVideoTrack track)
    {
        _remoteVideoTracks[cid] = track;
        _state = _state with
        {
            RemoteParticipants = _state.RemoteParticipants
                .Select(p => p.Cid == cid
                    ? p with { CameraReceiving = true }
                    : p)
                .ToList()
                .AsReadOnly(),
        };
        CommitState();
        NotifyRemoteVideoTrack(cid, track);
    }

    private void HandleRemoteVideoTrackRemoved(string cid, IRtcVideoTrack track)
    {
        if (_remoteVideoTracks.TryGetValue(cid, out var current))
        {
            if (!ReferenceEquals(current, track))
                return;
            _remoteVideoTracks.Remove(cid);
        }

        _state = _state with
        {
            RemoteParticipants = _state.RemoteParticipants
                .Select(p => p.Cid == cid
                    ? p with { CameraReceiving = false }
                    : p)
                .ToList()
                .AsReadOnly(),
        };
        CommitState();
        NotifyRemoteVideoTrack(cid, null);
    }

    private void NotifyRemoteVideoTrack(string cid, IRtcVideoTrack? track)
    {
        foreach (var listener in _remoteVideoTrackListeners.ToArray())
        {
            try { listener(cid, track); }
            catch { /* Renderer failures must not break the SDK. */ }
        }
    }

    private void HandlePeerConnectionChanged(string cid, string state)
    {
        var mappedState = state switch
        {
            "connecting" or "checking" => SerenadaPeerConnectionState.Connecting,
            "connected" or "completed" => SerenadaPeerConnectionState.Connected,
            "disconnected" => SerenadaPeerConnectionState.Disconnected,
            "failed" => SerenadaPeerConnectionState.Failed,
            "closed" => SerenadaPeerConnectionState.Closed,
            _ => SerenadaPeerConnectionState.New,
        };

        _state = _state with
        {
            RemoteParticipants = _state.RemoteParticipants
                .Select(p => p.Cid == cid
                    ? p with { ConnectionState = mappedState }
                    : p)
                .ToList()
                .AsReadOnly(),
        };
        UpdateDiagnostics(d => d with
        {
            IceConnectionState = _mediaEngine?.AggregateIceConnectionState,
            PeerConnectionState = _mediaEngine?.AggregatePeerConnectionState,
        });
        CommitState();
    }

    private RemoteParticipant MergeRemoteRuntimeState(RemoteParticipant participant)
    {
        var existing = _state.RemoteParticipants
            .FirstOrDefault(p => p.Cid == participant.Cid);
        return existing == null
            ? participant
            : participant with
            {
                CameraReceiving = existing.CameraReceiving,
                ContentReceiving = existing.ContentReceiving,
                ConnectionState = existing.ConnectionState,
                PresumedLost = existing.PresumedLost,
                AudioLevel = existing.AudioLevel,
            };
    }

    private void SaveRecoveryRecord(
        string reconnectToken,
        int? ttlMs,
        long? epoch)
    {
        if (string.IsNullOrWhiteSpace(_localCid) ||
            string.IsNullOrWhiteSpace(reconnectToken))
        {
            return;
        }

        _recoveryStorage.Save(new RecoveryRecord(
            RoomId: RoomId,
            Cid: _localCid,
            ReconnectToken: reconnectToken,
            LastEpoch: epoch ?? 0,
            SessionStartTs: _state.CallStartedAtMs
                ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            ExpiresAtMs: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                + (ttlMs ?? WebRtcResilienceConstants.ReconnectTokenTtlFallbackMs)));
    }

    private void Dispatch(Action action)
    {
        if (_disposed) return;
        if (_sessionContext == null ||
            ReferenceEquals(SynchronizationContext.Current, _sessionContext))
        {
            action();
            return;
        }

        _sessionContext.Post(_ =>
        {
            if (!_disposed)
                action();
        }, null);
    }

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
