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

    // Glare handling
    private bool _isMakingOffer;
    private string? _pendingLocalOfferId;
    private string? _acceptedRemoteOfferId;
    private string? _currentNegotiationId;

    // ICE restart
    private long _lastIceRestartAt;
    private bool _pendingIceRestart;

    // Offer timeout
    private CancellationTokenSource? _offerTimeoutCts;

    // Inbound tracks
    private IRtcVideoTrack? _remoteCameraTrack;
    private IRtcAudioTrack? _remoteAudioTrack;
    private IRtcVideoTrack? _remoteContentTrack;

    // Transceivers for independent content mode
    private IRtcRtpTransceiver? _cameraTransceiver;
    private IRtcRtpTransceiver? _contentTransceiver;

    public string RemoteCid { get; }
    public bool SupportsIndependentContentVideo => _enableIndependentContent;
    public bool IsReady => true;

    public string IceConnectionState => _pc.IceConnectionState.ToString().ToLowerInvariant();
    public string ConnectionState => _pc.ConnectionState.ToString().ToLowerInvariant();

    public bool IsPathDirect { get; private set; } = true;

    public IRtcVideoTrack? RemoteCameraTrack => _remoteCameraTrack;
    public IRtcVideoTrack? RemoteContentTrack => _remoteContentTrack;

    public PeerConnectionSlot(
        IRtcPeerConnectionFactory factory,
        string remoteCid,
        bool supportsIndependentContentVideo,
        IPeerConnectionSlotCallbacks callbacks,
        IRtcAudioTrack? localAudioTrack,
        IRtcVideoTrack? localVideoTrack,
        IRtcVideoTrack? localContentVideoTrack,
        ISerenadaLogger? logger)
    {
        _factory = factory;
        RemoteCid = remoteCid;
        _enableIndependentContent = supportsIndependentContentVideo;
        _callbacks = callbacks;
        _logger = logger;

        var config = new RtcConfiguration
        {
            ContinualGatheringPolicy = true,
        };

        _pc = _factory.CreatePeerConnection(config, this);

        // Attach local tracks
        if (localAudioTrack != null)
            _pc.AddAudioTrack(localAudioTrack, ["local_stream"]);

        if (_enableIndependentContent && localContentVideoTrack != null)
        {
            // Independent content mode: camera + content on separate transceivers
            _cameraTransceiver = _pc.AddTransceiver(RtcMediaType.Video,
                RtcTransceiverDirection.SendRecv);
            _contentTransceiver = _pc.AddTransceiver(RtcMediaType.Video,
                RtcTransceiverDirection.SendRecv);

            // Set content encoding parameters
            _contentTransceiver.Sender.SetParameters(new RtcRtpParameters
            {
                MaxBitrateBps = 1_500_000,
                MaxFramerate = 5,
            });
        }
        else if (localVideoTrack != null)
        {
            // Legacy single-video mode
            _pc.AddVideoTrack(localVideoTrack, ["local_stream"]);
        }
    }

    // ── Offer / Answer lifecycle ─────────────────────────────

    public async Task<RtcSessionDescription> CreateOfferAsync()
    {
        CancelOfferTimeout();
        _isMakingOffer = true;

        var offer = await _pc.CreateOfferAsync();
        _pendingLocalOfferId = GenerateNegotiationId();
        _currentNegotiationId = _pendingLocalOfferId;

        await _pc.SetLocalDescriptionAsync(offer);

        // Start offer timeout
        _offerTimeoutCts = new CancellationTokenSource();
        _ = OfferTimeoutAsync(_offerTimeoutCts.Token);

        return offer;
    }

    public async Task<RtcSessionDescription> CreateAnswerAsync()
    {
        var answer = await _pc.CreateAnswerAsync();
        _currentNegotiationId = _acceptedRemoteOfferId;

        await _pc.SetLocalDescriptionAsync(answer);
        _isMakingOffer = false;

        return answer;
    }

    public async Task SetRemoteDescriptionAsync(RtcSessionDescription desc)
    {
        if (desc.Type == RtcSdpType.Offer)
        {
            _acceptedRemoteOfferId = null; // Reset on new offer
        }

        await _pc.SetRemoteDescriptionAsync(desc);

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
        if (_pc.SignalingState == RtcSignalingState.Stable)
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
        CancelOfferTimeout();
        _pc.Close();
    }

    public void RestartIce()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (now - _lastIceRestartAt < WebRtcResilienceConstants.IceRestartCooldownMs)
        {
            _pendingIceRestart = true;
            return;
        }

        _pendingIceRestart = false;
        _lastIceRestartAt = now;
        _pc.RestartIce();

        Log(SerenadaLogLevel.Debug, "Slot", $"ICE restart for {RemoteCid}.");
    }

    public void Dispose()
    {
        Close();
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
        if (videoTrack != null && videoTrack.Id == _remoteCameraTrack?.Id)
            _remoteCameraTrack = null;
        if (videoTrack != null && videoTrack.Id == _remoteContentTrack?.Id)
            _remoteContentTrack = null;
        if (audioTrack != null && audioTrack.Id == _remoteAudioTrack?.Id)
            _remoteAudioTrack = null;
    }

    public void OnRenegotiationNeeded()
    {
        // The deterministic offer owner will create a new offer.
        // This is handled by PeerNegotiationEngine (Phase 4).
    }

    public void OnIceGatheringChange(RtcIceGatheringState state) { /* Tracked internally */ }

    // ── Internals ────────────────────────────────────────────

    internal void SetIceServers(RtcConfiguration config)
    {
        _pc.SetConfiguration(config);
    }

    private void CancelOfferTimeout()
    {
        _offerTimeoutCts?.Cancel();
        _offerTimeoutCts?.Dispose();
        _offerTimeoutCts = null;
    }

    private async Task OfferTimeoutAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(WebRtcResilienceConstants.OfferTimeoutMs, ct);
            Log(SerenadaLogLevel.Warning, "Slot", $"Offer timeout for {RemoteCid} — rolling back.");
            await _pc.RollbackLocalDescriptionAsync();
            _isMakingOffer = false;
            RestartIce();
        }
        catch (OperationCanceledException) { /* Expected */ }
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
