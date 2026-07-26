using Serenada.Core.Models;
using Serenada.Core.WebRtc;

namespace Serenada.Core.Call;

/// <summary>
/// Per-peer WebRTC connection wrapper. Manages a single <see cref="IRtcPeerConnection"/>
/// with one remote participant, handling offer/answer lifecycle, ICE candidate
/// buffering, glare handling, and independent content video routing.
///
/// Mirrors <c>PeerConnectionSlot</c> on Android and iOS.
/// </summary>
internal class PeerConnectionSlot : IPeerConnectionSlot, IRtcPeerConnectionObserver
{
    private readonly IPeerConnectionSlotCallbacks _callbacks;
    private readonly ISerenadaLogger? _logger;
    private readonly bool _enableIndependentContent;
    private readonly bool _videoMediaEnabled;
    private readonly bool _isOfferOwner;
    private readonly IRtcAudioTrack? _localAudioTrack;
    private IRtcVideoTrack? _localVideoTrack;
    private readonly IRtcVideoTrack? _localContentVideoTrack;
    private readonly Task _initializationTask;

    private readonly List<RtcIceCandidate> _pendingIceCandidates = [];

    private IRtcPeerConnection? _pc;
    private string? _currentNegotiationId;
    private bool _hasRemoteDescription;
    private bool _closed;

    // ICE restart
    private long _lastIceRestartAt;

    // Inbound tracks
    private IRtcVideoTrack? _remoteCameraTrack;
    private IRtcAudioTrack? _remoteAudioTrack;
    private IRtcVideoTrack? _remoteContentTrack;

    // Transceivers for independent content mode
    private IRtcRtpTransceiver? _cameraTransceiver;
    private IRtcRtpTransceiver? _contentTransceiver;
    private IRtcRtpSender? _audioSender;
    private IRtcRtpSender? _cameraSender;
    private IRtcRtpSender? _contentSender;

    public string RemoteCid { get; }
    public bool SupportsIndependentContentVideo => _enableIndependentContent;
    public bool IsReady => !_closed;

    public string IceConnectionState =>
        (_pc?.IceConnectionState ?? RtcIceConnectionState.New)
        .ToString()
        .ToLowerInvariant();
    public string ConnectionState =>
        (_pc?.ConnectionState ?? RtcPeerConnectionState.New)
        .ToString()
        .ToLowerInvariant();
    public string SignalingState =>
        (_pc?.SignalingState ?? RtcSignalingState.Stable)
        .ToString()
        .ToLowerInvariant();

    public bool IsPathDirect { get; private set; } = true;

    public IRtcVideoTrack? RemoteCameraTrack => _remoteCameraTrack;
    public IRtcVideoTrack? RemoteContentTrack => _remoteContentTrack;
    public string? CurrentNegotiationId => _currentNegotiationId;

    public PeerConnectionSlot(
        IRtcPeerConnectionFactory factory,
        string remoteCid,
        bool supportsIndependentContentVideo,
        IPeerConnectionSlotCallbacks callbacks,
        IRtcAudioTrack? localAudioTrack,
        IRtcVideoTrack? localVideoTrack,
        IRtcVideoTrack? localContentVideoTrack,
        bool videoMediaEnabled,
        bool isOfferOwner,
        RtcConfiguration rtcConfiguration,
        ISerenadaLogger? logger)
    {
        RemoteCid = remoteCid;
        _enableIndependentContent = supportsIndependentContentVideo;
        _callbacks = callbacks;
        _logger = logger;
        _videoMediaEnabled = videoMediaEnabled;
        _isOfferOwner = isOfferOwner;
        _localAudioTrack = localAudioTrack;
        _localVideoTrack = localVideoTrack;
        _localContentVideoTrack = localContentVideoTrack;
        _initializationTask = InitializeAsync(
            factory,
            rtcConfiguration with { ContinualGatheringPolicy = true });
    }

    // ── Offer / Answer lifecycle ─────────────────────────────

    public async Task<RtcSessionDescription> CreateOfferAsync()
    {
        var pc = await GetPeerConnectionAsync();
        _currentNegotiationId ??= GenerateNegotiationId();
        var offer = await pc.CreateOfferAsync();
        await pc.SetLocalDescriptionAsync(offer);
        return offer;
    }

    public async Task<RtcSessionDescription> CreateAnswerAsync()
    {
        var pc = await GetPeerConnectionAsync();
        var answer = await pc.CreateAnswerAsync();
        await pc.SetLocalDescriptionAsync(answer);
        return answer;
    }

    public async Task SetRemoteDescriptionAsync(RtcSessionDescription desc)
    {
        var pc = await GetPeerConnectionAsync();
        await pc.SetRemoteDescriptionAsync(desc);
        if (desc.Type == RtcSdpType.Offer && !_isOfferOwner)
            BindAnswererTransceivers(pc);
        _hasRemoteDescription = true;

        // Flush buffered ICE candidates
        foreach (var ice in _pendingIceCandidates)
            await pc.AddIceCandidateAsync(ice);
        _pendingIceCandidates.Clear();
    }

    public async Task SetLocalDescriptionAsync(RtcSessionDescription desc)
    {
        var pc = await GetPeerConnectionAsync();
        await pc.SetLocalDescriptionAsync(desc);
    }

    public async Task AddIceCandidateAsync(RtcIceCandidate candidate)
    {
        if (!_hasRemoteDescription)
        {
            // No remote description yet — buffer
            if (_pendingIceCandidates.Count < WebRtcResilienceConstants.IceCandidateBufferMax)
                _pendingIceCandidates.Add(candidate);
        }
        else
        {
            var pc = await GetPeerConnectionAsync();
            await pc.AddIceCandidateAsync(candidate);
        }
    }

    public void Close()
    {
        if (_closed) return;
        _closed = true;
        if (_remoteCameraTrack is { } cameraTrack)
        {
            _remoteCameraTrack = null;
            _callbacks.OnRemoteVideoTrackRemoved(RemoteCid, cameraTrack);
        }
        _remoteContentTrack = null;
        _remoteAudioTrack = null;
        _pc?.Close();
    }

    public void RestartIce()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (now - _lastIceRestartAt < WebRtcResilienceConstants.IceRestartCooldownMs)
        {
            return;
        }

        _lastIceRestartAt = now;
        _pc?.RestartIce();

        Log(SerenadaLogLevel.Debug, "Slot", $"ICE restart for {RemoteCid}.");
    }

    public void Dispose()
    {
        Close();
    }

    public void SetNegotiationId(string negotiationId)
    {
        if (_currentNegotiationId != negotiationId)
            _hasRemoteDescription = false;
        _currentNegotiationId = negotiationId;
    }

    public void SetLocalVideoTrack(IRtcVideoTrack? track)
    {
        _localVideoTrack = track;
        _cameraSender?.SetVideoTrack(track);
    }

    // ── IRtcPeerConnectionObserver ───────────────────────────

    public void OnIceCandidate(RtcIceCandidate candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate.Candidate))
            return; // End-of-candidates marker

        _callbacks.SendSignaling("ice", RemoteCid, new
        {
            candidate = new
            {
                candidate = candidate.Candidate,
                sdpMid = candidate.SdpMid,
                sdpMLineIndex = candidate.SdpMLineIndex,
            },
            offerId = _currentNegotiationId,
        });
    }

    public void OnIceCandidatesRemoved(RtcIceCandidate[] candidates) { /* Not used */ }

    public void OnIceConnectionChange(RtcIceConnectionState state)
    {
        var stateStr = state.ToString().ToLowerInvariant();
        _callbacks.OnIceConnectionChanged(RemoteCid, stateStr);
    }

    public void OnConnectionChange(RtcPeerConnectionState state)
    {
        var stateStr = state.ToString().ToLowerInvariant();
        _callbacks.OnConnectionChanged(RemoteCid, stateStr);
    }

    public void OnSignalingChange(RtcSignalingState state) { /* Tracked internally */ }

    public void OnAddTrack(IRtcVideoTrack? videoTrack, IRtcAudioTrack? audioTrack, string streamId)
    {
        if (videoTrack != null)
        {
            // Route to camera or content role based on transceiver binding
            if (_contentTransceiver?.ReceiverVideoTrack?.Id == videoTrack.Id)
            {
                _remoteContentTrack = videoTrack;
                Log(SerenadaLogLevel.Info, "Slot", $"Content track from {RemoteCid}.");
            }
            else
            {
                _remoteCameraTrack = videoTrack;
                _callbacks.OnRemoteVideoTrackAdded(RemoteCid, videoTrack);
                Log(SerenadaLogLevel.Info, "Slot", $"Camera track from {RemoteCid}.");
            }
        }

        if (audioTrack != null)
        {
            _remoteAudioTrack = audioTrack;
            _callbacks.OnRemoteAudioTrackAdded(RemoteCid, audioTrack);
            Log(SerenadaLogLevel.Info, "Slot", $"Audio track from {RemoteCid}.");
        }
    }

    public void OnRemoveTrack(IRtcVideoTrack? videoTrack, IRtcAudioTrack? audioTrack)
    {
        if (videoTrack != null && ReferenceEquals(videoTrack, _remoteCameraTrack))
        {
            _remoteCameraTrack = null;
            _callbacks.OnRemoteVideoTrackRemoved(RemoteCid, videoTrack);
        }
        if (videoTrack != null && ReferenceEquals(videoTrack, _remoteContentTrack))
            _remoteContentTrack = null;
        if (audioTrack != null && ReferenceEquals(audioTrack, _remoteAudioTrack))
            _remoteAudioTrack = null;
    }

    public void OnRenegotiationNeeded()
    {
        _callbacks.OnRenegotiationNeeded(RemoteCid);
    }

    public void OnIceGatheringChange(RtcIceGatheringState state) { /* Tracked internally */ }

    // ── Internals ────────────────────────────────────────────

    internal void SetIceServers(RtcConfiguration config)
    {
        _pc?.SetConfiguration(config);
    }

    private async Task InitializeAsync(
        IRtcPeerConnectionFactory factory,
        RtcConfiguration rtcConfiguration)
    {
        var pc = await factory.CreatePeerConnectionAsync(rtcConfiguration, this);
        if (_closed)
        {
            pc.Dispose();
            return;
        }

        _pc = pc;
        if (!_isOfferOwner)
            return;

        var audioTransceiver = pc.AddTransceiver(
            RtcMediaType.Audio,
            RtcTransceiverDirection.SendRecv);
        _audioSender = audioTransceiver.Sender;
        _audioSender.SetAudioTrack(_localAudioTrack);

        if (!_videoMediaEnabled)
            return;

        _cameraTransceiver = pc.AddTransceiver(
            RtcMediaType.Video,
            RtcTransceiverDirection.SendRecv);
        _cameraSender = _cameraTransceiver.Sender;
        _cameraSender.SetVideoTrack(_localVideoTrack);

        if (!_enableIndependentContent)
            return;

        _contentTransceiver = pc.AddTransceiver(
            RtcMediaType.Video,
            RtcTransceiverDirection.SendRecv);
        _contentSender = _contentTransceiver.Sender;
        _contentSender.SetVideoTrack(_localContentVideoTrack);
        _contentSender.SetParameters(new RtcRtpParameters
        {
            MaxBitrateBps = 1_500_000,
            MaxFramerate = 5,
        });
    }

    private void BindAnswererTransceivers(IRtcPeerConnection pc)
    {
        var audioTransceiver = pc.Transceivers
            .FirstOrDefault(transceiver =>
                transceiver.MediaType == RtcMediaType.Audio);
        if (audioTransceiver != null)
        {
            audioTransceiver.Direction = RtcTransceiverDirection.SendRecv;
            _audioSender = audioTransceiver.Sender;
            _audioSender.SetAudioTrack(_localAudioTrack);
        }

        var videoTransceivers = pc.Transceivers
            .Where(transceiver =>
                transceiver.MediaType == RtcMediaType.Video)
            .ToList();
        if (!_videoMediaEnabled)
        {
            foreach (var transceiver in videoTransceivers)
                transceiver.Direction = RtcTransceiverDirection.Inactive;
            return;
        }

        if (videoTransceivers.Count > 0)
        {
            _cameraTransceiver = videoTransceivers[0];
            _cameraTransceiver.Direction = RtcTransceiverDirection.SendRecv;
            _cameraSender = _cameraTransceiver.Sender;
            _cameraSender.SetVideoTrack(_localVideoTrack);
        }

        if (_enableIndependentContent && videoTransceivers.Count > 1)
        {
            _contentTransceiver = videoTransceivers[1];
            _contentTransceiver.Direction = RtcTransceiverDirection.SendRecv;
            _contentSender = _contentTransceiver.Sender;
            _contentSender.SetVideoTrack(_localContentVideoTrack);
            _contentSender.SetParameters(new RtcRtpParameters
            {
                MaxBitrateBps = 1_500_000,
                MaxFramerate = 5,
            });
        }
    }

    private async Task<IRtcPeerConnection> GetPeerConnectionAsync()
    {
        await _initializationTask;
        if (_closed || _pc == null)
            throw new ObjectDisposedException(nameof(PeerConnectionSlot));
        return _pc;
    }

    private static string GenerateNegotiationId()
    {
        return Guid.NewGuid().ToString("N")[..12];
    }

    private void Log(SerenadaLogLevel level, string tag, string message)
    {
        _logger?.Log(level, tag, message);
    }
}
