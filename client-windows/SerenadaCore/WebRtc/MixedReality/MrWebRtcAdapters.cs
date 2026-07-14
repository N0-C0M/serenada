using Microsoft.MixedReality.WebRTC;
using Serenada.Core.WebRtc;

// ============================================================================
// Adapters implementing Serenada.Core.WebRtc interfaces
// by wrapping Microsoft.MixedReality.WebRTC v2.0.2.
//
// MR-WebRTC API notes:
//   - Tracks use "Name" (not "Id")
//   - IceServer uses TurnUserName/TurnPassword (not Username/Password)
//   - RemoteVideoTrack.Enabled is read-only
//   - VideoTrackRemovedDelegate has (Transceiver, RemoteVideoTrack) params
//   - AudioTrackRemovedDelegate has (Transceiver, RemoteAudioTrack) params
// ============================================================================

namespace Serenada.Core.WebRtc.MixedReality;

public sealed class MrWebRtcPlatform : IRtcPlatform
{
    public bool IsSupported => true;
    public void Initialize() { /* MR-WebRTC auto-initializes */ }
    public IRtcPeerConnectionFactory CreateFactory() => new MrPeerConnectionFactory();
}

// ============================================================================
// Factory
// ============================================================================

internal sealed class MrPeerConnectionFactory : IRtcPeerConnectionFactory
{
    public IRtcPeerConnection CreatePeerConnection(
        RtcConfiguration config, IRtcPeerConnectionObserver observer)
    {
        var pc = new MrPeerConnection(observer);
        pc.InitializeAsync(MrAdapterHelper.ToNativeConfig(config)).GetAwaiter().GetResult();
        return pc;
    }

    public IRtcVideoSource CreateVideoSource(bool isScreencast)
    {
        if (isScreencast)
            return new MrExternalVideoSource();

        var devices = DeviceVideoTrackSource.GetCaptureDevicesAsync().GetAwaiter().GetResult();
        if (devices.Count == 0)
            throw new InvalidOperationException("No video capture devices found.");

        var native = DeviceVideoTrackSource.CreateAsync(
            new LocalVideoDeviceInitConfig { videoDevice = devices[0] })
            .GetAwaiter().GetResult();

        return new MrDeviceVideoSource(native);
    }

    public IRtcAudioSource CreateAudioSource() => new MrAudioSource();

    public IRtcVideoTrack CreateVideoTrack(string id, IRtcVideoSource source) => source switch
    {
        MrDeviceVideoSource dev => new MrLocalVideoTrack(
            LocalVideoTrack.CreateFromSource(dev.NativeSource,
                new LocalVideoTrackInitConfig { trackName = id })),
        MrExternalVideoSource ext => ext.CreateTrack(id),
        _ => throw new ArgumentException($"Unknown source: {source.GetType()}", nameof(source)),
    };

    public IRtcAudioTrack CreateAudioTrack(string id, IRtcAudioSource source) =>
        new MrLocalAudioTrack(id);

    public IRtcMediaStream CreateLocalMediaStream(string id) =>
        new MrMediaStream(id);

    public void Dispose() { }
}

// ============================================================================
// PeerConnection
// ============================================================================

internal sealed class MrPeerConnection : IRtcPeerConnection
{
    private PeerConnection? _pc;
    private readonly IRtcPeerConnectionObserver _observer;

    private TaskCompletionSource<RtcSessionDescription>? _offerTcs;
    private TaskCompletionSource<RtcSessionDescription>? _answerTcs;

    public RtcIceConnectionState IceConnectionState { get; private set; } = RtcIceConnectionState.New;
    public RtcPeerConnectionState ConnectionState { get; private set; } = RtcPeerConnectionState.New;
    public RtcSignalingState SignalingState { get; private set; } = RtcSignalingState.Stable;
    public RtcIceGatheringState IceGatheringState { get; private set; } = RtcIceGatheringState.New;
    public IReadOnlyList<IRtcRtpTransceiver> Transceivers { get; private set; } = [];

    public MrPeerConnection(IRtcPeerConnectionObserver observer) => _observer = observer;

    internal async Task InitializeAsync(PeerConnectionConfiguration config)
    {
        _pc = new PeerConnection();

        _pc.Connected += () =>
        {
            ConnectionState = RtcPeerConnectionState.Connected;
            _observer.OnConnectionChange(ConnectionState);
        };

        _pc.IceStateChanged += (state) =>
        {
            IceConnectionState = MrAdapterHelper.FromNativeIceState(state);
            _observer.OnIceConnectionChange(IceConnectionState);
        };

        _pc.IceGatheringStateChanged += (state) =>
        {
            IceGatheringState = MrAdapterHelper.FromNativeGatheringState(state);
            _observer.OnIceGatheringChange(IceGatheringState);
        };

        _pc.LocalSdpReadytoSend += (sdp) =>
        {
            var desc = MrAdapterHelper.FromNativeSdp(sdp);
            if (sdp.Type == SdpMessageType.Offer)
                _offerTcs?.TrySetResult(desc);
            else
                _answerTcs?.TrySetResult(desc);
        };

        _pc.IceCandidateReadytoSend += (candidate) =>
            _observer.OnIceCandidate(MrAdapterHelper.FromNativeIce(candidate));

        _pc.RenegotiationNeeded += () => _observer.OnRenegotiationNeeded();

        _pc.VideoTrackAdded += (track) =>
            _observer.OnAddTrack(new MrRemoteVideoTrack(track), null, "remote");

        _pc.AudioTrackAdded += (track) =>
            _observer.OnAddTrack(null, new MrRemoteAudioTrack(track), "remote");

        _pc.VideoTrackRemoved += (_, track) =>
            _observer.OnRemoveTrack(new MrRemoteVideoTrack(track), null);

        _pc.AudioTrackRemoved += (_, track) =>
            _observer.OnRemoveTrack(null, new MrRemoteAudioTrack(track));

        _pc.TransceiverAdded += (_) =>
        {
            if (_pc != null)
                Transceivers = _pc.Transceivers
                    .Select(t => (IRtcRtpTransceiver)new MrTransceiver(t))
                    .ToList().AsReadOnly();
        };

        await _pc.InitializeAsync(config);
    }

    public IRtcRtpSender AddVideoTrack(IRtcVideoTrack track, IReadOnlyList<string> streamIds)
    {
        // In MR-WebRTC, video tracks are added via transceivers, not directly.
        // The track is associated with a transceiver when AddTransceiver is called.
        return new MrRtpSender();
    }

    public IRtcRtpSender AddAudioTrack(IRtcAudioTrack track, IReadOnlyList<string> streamIds)
        => new MrRtpSender();

    public IRtcRtpTransceiver AddTransceiver(RtcMediaType mediaType, RtcTransceiverDirection direction)
    {
        if (_pc == null) throw new ObjectDisposedException(nameof(MrPeerConnection));
        var kind = mediaType == RtcMediaType.Video ? MediaKind.Video : MediaKind.Audio;
        var init = new TransceiverInitSettings
        {
            Name = mediaType == RtcMediaType.Video ? "video" : "audio",
            InitialDesiredDirection = MrAdapterHelper.ToNativeDirection(direction),
        };
        return new MrTransceiver(_pc.AddTransceiver(kind, init));
    }

    public bool RemoveTrack(IRtcRtpSender sender) => true;

    public async Task<RtcSessionDescription> CreateOfferAsync()
    {
        if (_pc == null) throw new ObjectDisposedException(nameof(MrPeerConnection));
        _offerTcs = new TaskCompletionSource<RtcSessionDescription>();
        _pc.CreateOffer();
        return await _offerTcs.Task;
    }

    public async Task<RtcSessionDescription> CreateAnswerAsync()
    {
        if (_pc == null) throw new ObjectDisposedException(nameof(MrPeerConnection));
        _answerTcs = new TaskCompletionSource<RtcSessionDescription>();
        _pc.CreateAnswer();
        return await _answerTcs.Task;
    }

    public Task SetLocalDescriptionAsync(RtcSessionDescription desc)
        => Task.CompletedTask; // MR-WebRTC handles local SDP internally

    public async Task SetRemoteDescriptionAsync(RtcSessionDescription desc)
    {
        if (_pc != null)
            await _pc.SetRemoteDescriptionAsync(MrAdapterHelper.ToNativeSdp(desc));
    }

    public Task AddIceCandidateAsync(RtcIceCandidate candidate)
    {
        _pc?.AddIceCandidate(MrAdapterHelper.ToNativeIce(candidate));
        return Task.CompletedTask;
    }

    public Task RollbackLocalDescriptionAsync() => Task.CompletedTask;

    public Task<IReadOnlyList<RtcStatsEntry>> GetStatsAsync()
        => Task.FromResult<IReadOnlyList<RtcStatsEntry>>([]);

    public void Close()
    {
        _pc?.Close();
        _pc?.Dispose();
        _pc = null;
    }

    public void SetConfiguration(RtcConfiguration config) { /* Set at init time */ }

    public void RestartIce() => _pc?.CreateOffer();

    public void Dispose()
    {
        Close();
    }
}

// ============================================================================
// Media sources
// ============================================================================

internal sealed class MrDeviceVideoSource : IRtcVideoSource
{
    public DeviceVideoTrackSource NativeSource { get; }
    public bool IsScreencast => false;
    public MrDeviceVideoSource(DeviceVideoTrackSource native) => NativeSource = native;
    public void OnFrameCaptured(IntPtr _, int __, int ___, long ____) { }
}

internal sealed class MrExternalVideoSource : IRtcVideoSource
{
    public bool IsScreencast => true;

    public MrLocalVideoTrack CreateTrack(string id)
    {
        var extSource = ExternalVideoTrackSource.CreateFromArgb32Callback(
            OnArgb32FrameRequest);
        var track = LocalVideoTrack.CreateFromSource(extSource,
            new LocalVideoTrackInitConfig { trackName = id });
        return new MrLocalVideoTrack(track);
    }

    private void OnArgb32FrameRequest(in FrameRequest request) { }

    public void OnFrameCaptured(IntPtr frameData, int width, int height, long timestampUs) { }
}

internal sealed class MrAudioSource : IRtcAudioSource { }

// ============================================================================
// Media tracks — MR-WebRTC uses "Name" not "Id"
// ============================================================================

internal sealed class MrLocalVideoTrack : IRtcVideoTrack
{
    public LocalVideoTrack NativeTrack { get; }
    public MrLocalVideoTrack(LocalVideoTrack track) => NativeTrack = track;
    public string Id => NativeTrack.Name;
    public bool Enabled { get => NativeTrack.Enabled; set => NativeTrack.Enabled = value; }
    public RtcTrackState State => NativeTrack.Enabled ? RtcTrackState.Live : RtcTrackState.Ended;
    public void AddSink(IRtcVideoSink _) { }
    public void RemoveSink(IRtcVideoSink _) { }
}

internal sealed class MrRemoteVideoTrack : IRtcVideoTrack
{
    public RemoteVideoTrack NativeTrack { get; }
    public MrRemoteVideoTrack(RemoteVideoTrack track) => NativeTrack = track;
    public string Id => NativeTrack.Name;
    public bool Enabled => NativeTrack.Enabled;
    bool IRtcVideoTrack.Enabled { get => NativeTrack.Enabled; set { /* read-only */ } }
    public RtcTrackState State => NativeTrack.Enabled ? RtcTrackState.Live : RtcTrackState.Ended;
    public void AddSink(IRtcVideoSink _) { }
    public void RemoveSink(IRtcVideoSink _) { }
}

internal sealed class MrLocalAudioTrack : IRtcAudioTrack
{
    public string Id { get; }
    public MrLocalAudioTrack(string id) => Id = id;
    public bool Enabled { get; set; } = true;
    public RtcTrackState State => Enabled ? RtcTrackState.Live : RtcTrackState.Ended;
}

internal sealed class MrRemoteAudioTrack : IRtcAudioTrack
{
    public RemoteAudioTrack NativeTrack { get; }
    public MrRemoteAudioTrack(RemoteAudioTrack track) => NativeTrack = track;
    public string Id => NativeTrack.Name;
    public bool Enabled => NativeTrack.Enabled;
    bool IRtcAudioTrack.Enabled { get => NativeTrack.Enabled; set { /* read-only */ } }
    public RtcTrackState State => NativeTrack.Enabled ? RtcTrackState.Live : RtcTrackState.Ended;
}

// ============================================================================
// Media stream
// ============================================================================

internal sealed class MrMediaStream : IRtcMediaStream
{
    public string Id { get; }
    public IReadOnlyList<IRtcVideoTrack> VideoTracks { get; } = [];
    public IReadOnlyList<IRtcAudioTrack> AudioTracks { get; } = [];
    public MrMediaStream(string id) => Id = id;
}

// ============================================================================
// RTP
// ============================================================================

internal sealed class MrTransceiver : IRtcRtpTransceiver
{
    public Transceiver NativeTransceiver { get; }
    public MrTransceiver(Transceiver t) => NativeTransceiver = t;
    public string Mid => NativeTransceiver.Name;
    public RtcMediaType MediaType => NativeTransceiver.MediaKind == MediaKind.Audio
        ? RtcMediaType.Audio : RtcMediaType.Video;
    public IRtcRtpSender Sender => new MrRtpSender();
    public IRtcVideoTrack? ReceiverVideoTrack => null;
    public IRtcAudioTrack? ReceiverAudioTrack => null;

    public RtcTransceiverDirection Direction
    {
        get => MrAdapterHelper.FromNativeDirection(NativeTransceiver.DesiredDirection);
        set => NativeTransceiver.DesiredDirection = MrAdapterHelper.ToNativeDirection(value);
    }
}

internal sealed class MrRtpSender : IRtcRtpSender
{
    public string? TrackId => null;
    public IRtcVideoTrack? VideoTrack => null;
    public IRtcAudioTrack? AudioTrack => null;
    public void SetParameters(RtcRtpParameters _) { }
}

// ============================================================================
// Conversion helpers
// ============================================================================

internal static class MrAdapterHelper
{
    public static PeerConnectionConfiguration ToNativeConfig(RtcConfiguration config)
    {
        var native = new PeerConnectionConfiguration();
        foreach (var server in config.IceServers)
        {
            native.IceServers.Add(new IceServer
            {
                Urls = server.Urls.ToList(),
                TurnUserName = server.Username ?? "",
                TurnPassword = server.Password ?? "",
            });
        }
        return native;
    }

    public static RtcSessionDescription FromNativeSdp(SdpMessage sdp) => new()
    {
        Type = sdp.Type == SdpMessageType.Offer ? RtcSdpType.Offer : RtcSdpType.Answer,
        Sdp = sdp.Content,
    };

    public static SdpMessage ToNativeSdp(RtcSessionDescription desc) => new()
    {
        Type = desc.Type == RtcSdpType.Offer ? SdpMessageType.Offer : SdpMessageType.Answer,
        Content = desc.Sdp,
    };

    public static RtcIceCandidate FromNativeIce(IceCandidate c) => new()
    {
        SdpMid = c.SdpMid,
        SdpMLineIndex = c.SdpMlineIndex,
        Candidate = c.Content,
    };

    public static IceCandidate ToNativeIce(RtcIceCandidate c) => new()
    {
        SdpMid = c.SdpMid,
        SdpMlineIndex = c.SdpMLineIndex,
        Content = c.Candidate,
    };

    public static Transceiver.Direction ToNativeDirection(RtcTransceiverDirection d) => d switch
    {
        RtcTransceiverDirection.SendRecv => Transceiver.Direction.SendReceive,
        RtcTransceiverDirection.SendOnly => Transceiver.Direction.SendOnly,
        RtcTransceiverDirection.RecvOnly => Transceiver.Direction.ReceiveOnly,
        _ => Transceiver.Direction.Inactive,
    };

    public static RtcTransceiverDirection FromNativeDirection(Transceiver.Direction d) => d switch
    {
        Transceiver.Direction.SendReceive => RtcTransceiverDirection.SendRecv,
        Transceiver.Direction.SendOnly => RtcTransceiverDirection.SendOnly,
        Transceiver.Direction.ReceiveOnly => RtcTransceiverDirection.RecvOnly,
        _ => RtcTransceiverDirection.Inactive,
    };

    public static RtcIceConnectionState FromNativeIceState(IceConnectionState s) => s switch
    {
        IceConnectionState.New => RtcIceConnectionState.New,
        IceConnectionState.Checking => RtcIceConnectionState.Checking,
        IceConnectionState.Connected => RtcIceConnectionState.Connected,
        IceConnectionState.Completed => RtcIceConnectionState.Completed,
        IceConnectionState.Failed => RtcIceConnectionState.Failed,
        IceConnectionState.Disconnected => RtcIceConnectionState.Disconnected,
        IceConnectionState.Closed => RtcIceConnectionState.Closed,
        _ => RtcIceConnectionState.Closed,
    };

    public static RtcIceGatheringState FromNativeGatheringState(IceGatheringState s) => s switch
    {
        IceGatheringState.New => RtcIceGatheringState.New,
        IceGatheringState.Gathering => RtcIceGatheringState.Gathering,
        IceGatheringState.Complete => RtcIceGatheringState.Complete,
        _ => RtcIceGatheringState.New,
    };
}
