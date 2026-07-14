namespace Serenada.Core.WebRtc;

// ============================================================================
// Peer connection interface — the main WebRTC primitive.
// Implemented by the C++/CLI bridge wrapping libwebrtc's PeerConnection.
// ============================================================================

/// <summary>
/// A single RTCPeerConnection — represents a direct media path to one remote peer.
/// </summary>
public interface IRtcPeerConnection : IDisposable
{
    /// <summary>Current ICE connection state.</summary>
    RtcIceConnectionState IceConnectionState { get; }

    /// <summary>Current peer connection state.</summary>
    RtcPeerConnectionState ConnectionState { get; }

    /// <summary>Current signaling state.</summary>
    RtcSignalingState SignalingState { get; }

    /// <summary>ICE gathering state.</summary>
    RtcIceGatheringState IceGatheringState { get; }

    // ── Setup ─────────────────────────────────────────────────

    /// <summary>Add a video track to be sent to the remote peer.</summary>
    IRtcRtpSender AddVideoTrack(IRtcVideoTrack track, IReadOnlyList<string> streamIds);

    /// <summary>Add an audio track to be sent to the remote peer.</summary>
    IRtcRtpSender AddAudioTrack(IRtcAudioTrack track, IReadOnlyList<string> streamIds);

    /// <summary>Add a transceiver (for independent content video).</summary>
    IRtcRtpTransceiver AddTransceiver(RtcMediaType mediaType, RtcTransceiverDirection direction);

    /// <summary>Remove a track/sender.</summary>
    bool RemoveTrack(IRtcRtpSender sender);

    /// <summary>All current transceivers.</summary>
    IReadOnlyList<IRtcRtpTransceiver> Transceivers { get; }

    // ── Negotiation ───────────────────────────────────────────

    /// <summary>Create an SDP offer.</summary>
    Task<RtcSessionDescription> CreateOfferAsync();

    /// <summary>Create an SDP answer.</summary>
    Task<RtcSessionDescription> CreateAnswerAsync();

    /// <summary>Set the local SDP description.</summary>
    Task SetLocalDescriptionAsync(RtcSessionDescription desc);

    /// <summary>Set the remote SDP description.</summary>
    Task SetRemoteDescriptionAsync(RtcSessionDescription desc);

    /// <summary>Add a remote ICE candidate.</summary>
    Task AddIceCandidateAsync(RtcIceCandidate candidate);

    /// <summary>Roll back any pending local description.</summary>
    Task RollbackLocalDescriptionAsync();

    // ── Stats ─────────────────────────────────────────────────

    /// <summary>Collect WebRTC stats for this connection.</summary>
    Task<IReadOnlyList<RtcStatsEntry>> GetStatsAsync();

    // ── Lifecycle ─────────────────────────────────────────────

    /// <summary>Close the peer connection gracefully.</summary>
    void Close();

    /// <summary>Set the ICE servers configuration.</summary>
    void SetConfiguration(RtcConfiguration config);

    /// <summary>Restart ICE (triggers new candidate gathering).</summary>
    void RestartIce();
}

/// <summary>
/// Factory for creating peer connections and media sources.
/// </summary>
public interface IRtcPeerConnectionFactory : IDisposable
{
    /// <summary>Create a new peer connection.</summary>
    IRtcPeerConnection CreatePeerConnection(RtcConfiguration config, IRtcPeerConnectionObserver observer);

    /// <summary>Create a video source (for camera or screen capture).</summary>
    IRtcVideoSource CreateVideoSource(bool isScreencast);

    /// <summary>Create an audio source.</summary>
    IRtcAudioSource CreateAudioSource();

    /// <summary>Create a video track from a source.</summary>
    IRtcVideoTrack CreateVideoTrack(string id, IRtcVideoSource source);

    /// <summary>Create an audio track from a source.</summary>
    IRtcAudioTrack CreateAudioTrack(string id, IRtcAudioSource source);

    /// <summary>
    /// Create a media stream that holds local tracks.
    /// In Unified Plan (what Serenada uses), tracks are added via
    /// AddTrack/AddTransceiver on the peer connection, not via streams.
    /// This is provided for local stream aggregation / snapshot capture.
    /// </summary>
    IRtcMediaStream CreateLocalMediaStream(string id);
}

/// <summary>
/// Platform-specific WebRTC initialization.
/// Provides the concrete <see cref="IRtcPeerConnectionFactory"/> implementation.
/// </summary>
public interface IRtcPlatform
{
    /// <summary>
    /// Initialize the WebRTC native layer. Must be called once before creating
    /// any factories.
    /// </summary>
    void Initialize();

    /// <summary>
    /// Create the peer connection factory. Requires an active Windows
    /// DispatcherQueue for the UI thread.
    /// </summary>
    IRtcPeerConnectionFactory CreateFactory();

    /// <summary>
    /// Check whether WebRTC is available on this device.
    /// Returns <c>false</c> if the native DLL failed to load.
    /// </summary>
    bool IsSupported { get; }
}
