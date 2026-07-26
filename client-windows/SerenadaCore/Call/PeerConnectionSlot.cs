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
    private readonly IRtcPeerConnection _pc;
    private readonly IRtcPeerConnectionFactory _factory;
    private readonly IPeerConnectionSlotCallbacks _callbacks;
    private readonly ISerenadaLogger? _logger;
    private readonly bool _enableIndependentContent;

    private readonly List<RtcIceCandidate> _pendingIceCandidates = [];

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

    public string IceConnectionState => _pc.IceConnectionState.ToString().ToLowerInvariant();
    public string ConnectionState => _pc.ConnectionState.ToString().ToLowerInvariant();
    public string SignalingState => _pc.SignalingState.ToString().ToLowerInvariant();

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
        RtcConfiguration rtcConfiguration,
        ISerenadaLogger? logger)
    {
        _factory = factory;
        RemoteCid = remoteCid;
        _enableIndependentContent = supportsIndependentContentVideo;
        _callbacks = callbacks;
        _logger = logger;

        _pc = _factory.CreatePeerConnection(
            rtcConfiguration with { ContinualGatheringPolicy = true },
            this);

        var audioTransceiver = _pc.AddTransceiver(
            RtcMediaType.Audio,
            RtcTransceiverDirection.SendRecv);
        _audioSender = audioTransceiver.Sender;
        _audioSender.SetAudioTrack(localAudioTrack);

        if (videoMediaEnabled)
        {
            _cameraTransceiver = _pc.AddTransceiver(RtcMediaType.Video,
                RtcTransceiverDirection.SendRecv);
            _cameraSender = _cameraTransceiver.Sender;
            _cameraSender.SetVideoTrack(localVideoTrack);

            if (_enableIndependentContent)
            {
                _contentTransceiver = _pc.AddTransceiver(RtcMediaType.Video,
                    RtcTransceiverDirection.SendRecv);
                _contentSender = _contentTransceiver.Sender;
                _contentSender.SetVideoTrack(localContentVideoTrack);
                _contentSender.SetParameters(new RtcRtpParameters
                {
                    MaxBitrateBps = 1_500_000,
                    MaxFramerate = 5,
                });
            }
        }
    }

    // ── Offer / Answer lifecycle ─────────────────────────────

    public async Task<RtcSessionDescription> CreateOfferAsync()
    {
        _currentNegotiationId ??= GenerateNegotiationId();
        var offer = await _pc.CreateOfferAsync();
        await _pc.SetLocalDescriptionAsync(offer);
        return offer;
    }

    public async Task<RtcSessionDescription> CreateAnswerAsync()
    {
        var answer = await _pc.CreateAnswerAsync();
        await _pc.SetLocalDescriptionAsync(answer);
        return answer;
    }

    public async Task SetRemoteDescriptionAsync(RtcSessionDescription desc)
    {
        await _pc.SetRemoteDescriptionAsync(desc);
        _hasRemoteDescription = true;

        // Flush buffered ICE candidates
        foreach (var ice in _pendingIceCandidates)
            await _pc.AddIceCandidateAsync(ice);
        _pendingIceCandidates.Clear();
    }

    public async Task SetLocalDescriptionAsync(RtcSessionDescription desc)
    {
        await _pc.SetLocalDescriptionAsync(desc);
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
            await _pc.AddIceCandidateAsync(candidate);
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
        _pc.Close();
    }

    public void RestartIce()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (now - _lastIceRestartAt < WebRtcResilienceConstants.IceRestartCooldownMs)
        {
            return;
        }

        _lastIceRestartAt = now;
        _pc.RestartIce();

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
        _pc.SetConfiguration(config);
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
