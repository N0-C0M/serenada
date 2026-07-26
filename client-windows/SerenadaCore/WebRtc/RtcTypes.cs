namespace Serenada.Core.WebRtc;

// ============================================================================
// Core WebRTC types — managed abstractions over libwebrtc.
// The C++/CLI bridge (SerenadaWebRtcNative) provides concrete implementations
// of these interfaces by wrapping the native libwebrtc C++ objects.
//
// Mirrors the WebRTC API surface used by Android (org.webrtc.*) and
// iOS (GoogleWebRTC RTC* classes).
// ============================================================================

/// <summary>ICE server (STUN/TURN) configuration.</summary>
public sealed record RtcIceServer
{
    public IReadOnlyList<string> Urls { get; init; } = [];
    public string? Username { get; init; }
    public string? Password { get; init; }
}

/// <summary>Peer connection configuration.</summary>
public sealed record RtcConfiguration
{
    public IReadOnlyList<RtcIceServer> IceServers { get; init; } = [];
    public bool ContinualGatheringPolicy { get; init; } = true;
}

/// <summary>SDP session description.</summary>
public sealed record RtcSessionDescription
{
    public RtcSdpType Type { get; init; }
    public string Sdp { get; init; } = string.Empty;
}

/// <summary>SDP type.</summary>
public enum RtcSdpType
{
    Offer,
    PrAnswer,
    Answer,
    Rollback,
}

/// <summary>ICE candidate.</summary>
public sealed record RtcIceCandidate
{
    public string SdpMid { get; init; } = string.Empty;
    public int SdpMLineIndex { get; init; }
    public string Candidate { get; init; } = string.Empty;
}

/// <summary>ICE connection state.</summary>
public enum RtcIceConnectionState
{
    New,
    Checking,
    Connected,
    Completed,
    Failed,
    Disconnected,
    Closed,
}

/// <summary>Peer connection state.</summary>
public enum RtcPeerConnectionState
{
    New,
    Connecting,
    Connected,
    Disconnected,
    Failed,
    Closed,
}

/// <summary>Signaling state.</summary>
public enum RtcSignalingState
{
    Stable,
    HaveLocalOffer,
    HaveLocalPrAnswer,
    HaveRemoteOffer,
    HaveRemotePrAnswer,
    Closed,
}

/// <summary>Track state.</summary>
public enum RtcTrackState
{
    Live,
    Ended,
}

/// <summary>Video frame for rendering.</summary>
public interface IRtcVideoFrame
{
    int Width { get; }
    int Height { get; }
    int Stride { get; }
    long TimestampUs { get; }
    bool IsBlack { get; }

    /// <summary>Copy this ARGB32 frame into a managed destination buffer.</summary>
    void CopyTo(byte[] destination);
}

/// <summary>Video frame sink — receives frames for rendering.</summary>
public interface IRtcVideoSink
{
    void OnFrame(IRtcVideoFrame frame);
}

/// <summary>Media stream.</summary>
public interface IRtcMediaStream
{
    string Id { get; }
    IReadOnlyList<IRtcVideoTrack> VideoTracks { get; }
    IReadOnlyList<IRtcAudioTrack> AudioTracks { get; }
}

/// <summary>Video track.</summary>
public interface IRtcVideoTrack
{
    string Id { get; }
    bool Enabled { get; set; }
    RtcTrackState State { get; }
    void AddSink(IRtcVideoSink sink);
    void RemoveSink(IRtcVideoSink sink);
}

/// <summary>Audio track.</summary>
public interface IRtcAudioTrack
{
    string Id { get; }
    bool Enabled { get; set; }
    RtcTrackState State { get; }
}

/// <summary>Video source — produces frames from a camera or screen capture.</summary>
public interface IRtcVideoSource
{
    bool IsScreencast { get; }
    void OnFrameCaptured(IntPtr frameData, int width, int height, long timestampUs);
}

/// <summary>Audio source — produces audio from a microphone.</summary>
public interface IRtcAudioSource
{
}

/// <summary>RTP sender — controls encoding parameters.</summary>
public interface IRtcRtpSender
{
    string? TrackId { get; }
    IRtcVideoTrack? VideoTrack { get; }
    IRtcAudioTrack? AudioTrack { get; }
    void SetVideoTrack(IRtcVideoTrack? track);
    void SetAudioTrack(IRtcAudioTrack? track);
    void SetParameters(RtcRtpParameters parameters);
}

/// <summary>RTP encoding parameters.</summary>
public sealed record RtcRtpParameters
{
    public int? MaxBitrateBps { get; init; }
    public int? MaxFramerate { get; init; }
    public string? ScalabilityMode { get; init; }
}

/// <summary>RTP transceiver — combines a sender and receiver.</summary>
public interface IRtcRtpTransceiver
{
    string Mid { get; }
    RtcMediaType MediaType { get; }
    IRtcRtpSender Sender { get; }
    IRtcVideoTrack? ReceiverVideoTrack { get; }
    IRtcAudioTrack? ReceiverAudioTrack { get; }
    RtcTransceiverDirection Direction { get; set; }
}

/// <summary>Media type for a transceiver.</summary>
public enum RtcMediaType
{
    Audio,
    Video,
}

/// <summary>Transceiver direction.</summary>
public enum RtcTransceiverDirection
{
    SendRecv,
    SendOnly,
    RecvOnly,
    Inactive,
}

/// <summary>RTCPeerConnection observer callbacks.</summary>
public interface IRtcPeerConnectionObserver
{
    void OnIceCandidate(RtcIceCandidate candidate);
    void OnIceCandidatesRemoved(RtcIceCandidate[] candidates);
    void OnIceConnectionChange(RtcIceConnectionState state);
    void OnConnectionChange(RtcPeerConnectionState state);
    void OnSignalingChange(RtcSignalingState state);
    void OnAddTrack(IRtcVideoTrack? videoTrack, IRtcAudioTrack? audioTrack, string streamId);
    void OnRemoveTrack(IRtcVideoTrack? videoTrack, IRtcAudioTrack? audioTrack);
    void OnRenegotiationNeeded();
    void OnIceGatheringChange(RtcIceGatheringState state);
}

/// <summary>ICE gathering state.</summary>
public enum RtcIceGatheringState
{
    New,
    Gathering,
    Complete,
}

/// <summary>Data channel (unused by Serenada, placeholder).</summary>
public interface IRtcDataChannel
{
    string Label { get; }
    RtcDataChannelState State { get; }
}

/// <summary>Data channel state.</summary>
public enum RtcDataChannelState
{
    Connecting,
    Open,
    Closing,
    Closed,
}

/// <summary>Stats report entry.</summary>
public sealed record RtcStatsEntry
{
    public string Id { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public long TimestampUs { get; init; }
    public IReadOnlyDictionary<string, object> Values { get; init; } =
        new Dictionary<string, object>();
}
