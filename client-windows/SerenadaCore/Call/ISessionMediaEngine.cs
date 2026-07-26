using Serenada.Core.Models;
using Serenada.Core.Signaling;
using Serenada.Core.WebRtc;

namespace Serenada.Core.Call;

/// <summary>
/// Internal interface for the media engine — decouples <see cref="SerenadaSession"/>
/// from the concrete WebRTC implementation.
/// Mirrors <c>SessionMediaEngine</c> on Android/iOS.
/// </summary>
internal interface ISessionMediaEngine : IDisposable
{
    /// <summary>Raised when the local preview track is created or replaced.</summary>
    event Action<IRtcVideoTrack?> LocalVideoTrackChanged;

    /// <summary>Start local camera and microphone capture.</summary>
    Task StartLocalMediaAsync(bool startVideo);

    /// <summary>Stop all local capture and release resources.</summary>
    void Release();

    /// <summary>Enable or disable the local audio track.</summary>
    void SetAudioEnabled(bool enabled);

    /// <summary>Enable or disable the local video track.</summary>
    void SetVideoEnabled(bool enabled);

    /// <summary>Flip to the next camera mode.</summary>
    Task FlipCameraAsync();

    /// <summary>Set a specific camera mode.</summary>
    Task SetCameraModeAsync(CameraMode mode);

    /// <summary>Start screen sharing.</summary>
    Task StartScreenShareAsync();

    /// <summary>Stop screen sharing.</summary>
    Task StopScreenShareAsync();

    /// <summary>Provide ICE servers to all peer slots.</summary>
    void SetIceServers(IReadOnlyList<IceServer> servers);

    /// <summary>Check if ICE servers have been configured.</summary>
    bool HasIceServers { get; }

    /// <summary>Create a peer connection slot for a new remote participant.</summary>
    IPeerConnectionSlot CreateSlot(RemoteParticipant participant,
        IPeerConnectionSlotCallbacks callbacks);

    /// <summary>Remove a peer slot.</summary>
    void RemoveSlot(IPeerConnectionSlot slot);

    /// <summary>The local audio source (for audio level polling).</summary>
    IRtcAudioSource? LocalAudioSource { get; }

    /// <summary>The local video source (for snapshot capture).</summary>
    IRtcVideoSource? LocalVideoSource { get; }

    /// <summary>The local video track used for preview and outbound media.</summary>
    IRtcVideoTrack? LocalVideoTrack { get; }

    /// <summary>Whether multiple cameras are available.</summary>
    bool HasMultipleCameras { get; }

    /// <summary>Camera modes available on this device.</summary>
    IReadOnlyList<CameraMode> AvailableCameraModes { get; }

    /// <summary>Whether screen sharing is possible.</summary>
    bool CanScreenShare { get; }

    /// <summary>
    /// Whether this build can negotiate a second, independent content-video
    /// transceiver. This must describe the real implementation, not just the
    /// requested configuration.
    /// </summary>
    bool SupportsIndependentContentVideo { get; }

    /// <summary>The camera mode currently feeding the local video track.</summary>
    CameraMode CurrentCameraMode { get; }

    /// <summary>Aggregate ICE connection state across all slots.</summary>
    string AggregateIceConnectionState { get; }

    /// <summary>Aggregate peer connection state across all slots.</summary>
    string AggregatePeerConnectionState { get; }
}

/// <summary>
/// Callbacks from a peer connection slot back to the session/media engine.
/// </summary>
internal interface IPeerConnectionSlotCallbacks
{
    /// <summary>Called when the slot needs to send a signaling message.</summary>
    void SendSignaling(string type, string to, object? payload);

    /// <summary>Called when a remote video track is added.</summary>
    void OnRemoteVideoTrackAdded(string cid, IRtcVideoTrack track);

    /// <summary>Called when a remote video track is removed.</summary>
    void OnRemoteVideoTrackRemoved(string cid, IRtcVideoTrack track);

    /// <summary>Called when a remote audio track is added.</summary>
    void OnRemoteAudioTrackAdded(string cid, IRtcAudioTrack track);

    /// <summary>Called when the ICE connection state changes.</summary>
    void OnIceConnectionChanged(string cid, string state);

    /// <summary>Called when the peer connection state changes.</summary>
    void OnConnectionChanged(string cid, string state);

    /// <summary>Called when local track changes require renegotiation.</summary>
    void OnRenegotiationNeeded(string cid);

    /// <summary>Called when outbound media stalls.</summary>
    void OnOutboundMediaStalled(string cid);

    /// <summary>Called when inbound media liveness changes.</summary>
    void OnInboundLivenessChanged(string cid, bool cameraReceiving, bool contentReceiving);
}

/// <summary>
/// A single peer connection slot — manages one RTCPeerConnection with one remote participant.
/// </summary>
internal interface IPeerConnectionSlot : IDisposable
{
    /// <summary>The CID of the remote participant.</summary>
    string RemoteCid { get; }

    /// <summary>Whether this slot's peer supports independent content video.</summary>
    bool SupportsIndependentContentVideo { get; }

    /// <summary>Current ICE connection state.</summary>
    string IceConnectionState { get; }

    /// <summary>Current peer connection state.</summary>
    string ConnectionState { get; }

    /// <summary>Current WebRTC signaling state.</summary>
    string SignalingState { get; }

    /// <summary>Whether this slot is ready for negotiation.</summary>
    bool IsReady { get; }

    /// <summary>Begin creating an SDP offer for this peer.</summary>
    Task<RtcSessionDescription> CreateOfferAsync();

    /// <summary>Create an SDP answer for this peer.</summary>
    Task<RtcSessionDescription> CreateAnswerAsync();

    /// <summary>Set the remote SDP description.</summary>
    Task SetRemoteDescriptionAsync(RtcSessionDescription desc);

    /// <summary>Set the local SDP description.</summary>
    Task SetLocalDescriptionAsync(RtcSessionDescription desc);

    /// <summary>Add a remote ICE candidate.</summary>
    Task AddIceCandidateAsync(RtcIceCandidate candidate);

    /// <summary>Close the peer connection.</summary>
    void Close();

    /// <summary>Restart ICE (triggers new candidate gathering).</summary>
    void RestartIce();

    /// <summary>Whether the connection path is direct (not TURN-relayed).</summary>
    bool IsPathDirect { get; }

    /// <summary>The camera video track from this remote peer (if any).</summary>
    IRtcVideoTrack? RemoteCameraTrack { get; }

    /// <summary>The content (screen share) video track from this remote peer (if any).</summary>
    IRtcVideoTrack? RemoteContentTrack { get; }

    /// <summary>Negotiation identifier attached to local ICE candidates.</summary>
    string? CurrentNegotiationId { get; }

    /// <summary>Set the active offer identifier before applying SDP.</summary>
    void SetNegotiationId(string negotiationId);

    /// <summary>Replace the local camera track without recreating the slot.</summary>
    void SetLocalVideoTrack(IRtcVideoTrack? track);
}
