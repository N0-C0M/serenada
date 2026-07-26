using Microsoft.MixedReality.WebRTC;
using Serenada.Core.WebRtc;
using System.Runtime.InteropServices;

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
    public async Task<IRtcPeerConnection> CreatePeerConnectionAsync(
        RtcConfiguration config, IRtcPeerConnectionObserver observer)
    {
        var pc = new MrPeerConnection(observer);
        await pc.InitializeAsync(MrAdapterHelper.ToNativeConfig(config));
        return pc;
    }

    public async Task<IRtcVideoSource> CreateVideoSourceAsync(bool isScreencast)
    {
        if (isScreencast)
            return new MrExternalVideoSource();

        var devices = await DeviceVideoTrackSource.GetCaptureDevicesAsync();
        if (devices.Count == 0)
            throw new InvalidOperationException("No video capture devices found.");

        var native = await DeviceVideoTrackSource.CreateAsync(
            new LocalVideoDeviceInitConfig { videoDevice = devices[0] });

        return new MrDeviceVideoSource(native);
    }

    public async Task<IRtcAudioSource> CreateAudioSourceAsync()
    {
        var source = await DeviceAudioTrackSource.CreateAsync(
            new LocalAudioDeviceInitConfig());
        return new MrAudioSource(source);
    }

    public IRtcVideoTrack CreateVideoTrack(string id, IRtcVideoSource source) => source switch
    {
        MrDeviceVideoSource dev => new MrLocalVideoTrack(
            LocalVideoTrack.CreateFromSource(dev.NativeSource,
                new LocalVideoTrackInitConfig { trackName = id })),
        MrExternalVideoSource ext => ext.CreateTrack(id),
        _ => throw new ArgumentException($"Unknown source: {source.GetType()}", nameof(source)),
    };

    public IRtcAudioTrack CreateAudioTrack(string id, IRtcAudioSource source)
    {
        if (source is not MrAudioSource nativeSource)
            throw new ArgumentException($"Unknown source: {source.GetType()}", nameof(source));

        var track = LocalAudioTrack.CreateFromSource(nativeSource.NativeSource,
            new LocalAudioTrackInitConfig { trackName = id });
        return new MrLocalAudioTrack(track);
    }

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
    private readonly Dictionary<RemoteVideoTrack, MrRemoteVideoTrack> _remoteVideoTracks = [];
    private readonly Dictionary<RemoteAudioTrack, MrRemoteAudioTrack> _remoteAudioTracks = [];
    private bool _closed;

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
            var connectionState = state switch
            {
                Microsoft.MixedReality.WebRTC.IceConnectionState.Checking =>
                    RtcPeerConnectionState.Connecting,
                Microsoft.MixedReality.WebRTC.IceConnectionState.Connected
                    or Microsoft.MixedReality.WebRTC.IceConnectionState.Completed =>
                    RtcPeerConnectionState.Connected,
                Microsoft.MixedReality.WebRTC.IceConnectionState.Disconnected =>
                    RtcPeerConnectionState.Disconnected,
                Microsoft.MixedReality.WebRTC.IceConnectionState.Failed =>
                    RtcPeerConnectionState.Failed,
                Microsoft.MixedReality.WebRTC.IceConnectionState.Closed =>
                    RtcPeerConnectionState.Closed,
                _ => RtcPeerConnectionState.New,
            };
            if (connectionState != ConnectionState)
            {
                ConnectionState = connectionState;
                _observer.OnConnectionChange(ConnectionState);
            }
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
            {
                SetSignalingState(RtcSignalingState.HaveLocalOffer);
                _offerTcs?.TrySetResult(desc);
            }
            else
            {
                SetSignalingState(RtcSignalingState.Stable);
                _answerTcs?.TrySetResult(desc);
            }
        };

        _pc.IceCandidateReadytoSend += (candidate) =>
            _observer.OnIceCandidate(MrAdapterHelper.FromNativeIce(candidate));

        _pc.RenegotiationNeeded += () => _observer.OnRenegotiationNeeded();

        _pc.VideoTrackAdded += (track) =>
        {
            if (!_remoteVideoTracks.TryGetValue(track, out var wrapped))
            {
                wrapped = new MrRemoteVideoTrack(track);
                _remoteVideoTracks[track] = wrapped;
            }
            _observer.OnAddTrack(wrapped, null, "remote");
        };

        _pc.AudioTrackAdded += (track) =>
        {
            if (!_remoteAudioTracks.TryGetValue(track, out var wrapped))
            {
                wrapped = new MrRemoteAudioTrack(track);
                _remoteAudioTracks[track] = wrapped;
            }
            _observer.OnAddTrack(null, wrapped, "remote");
        };

        _pc.VideoTrackRemoved += (_, track) =>
        {
            if (!_remoteVideoTracks.Remove(track, out var wrapped))
                wrapped = new MrRemoteVideoTrack(track);
            _observer.OnRemoveTrack(wrapped, null);
        };

        _pc.AudioTrackRemoved += (_, track) =>
        {
            if (!_remoteAudioTracks.Remove(track, out var wrapped))
                wrapped = new MrRemoteAudioTrack(track);
            _observer.OnRemoveTrack(null, wrapped);
        };

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
        if (_pc == null) throw new ObjectDisposedException(nameof(MrPeerConnection));
        var transceiver = new MrTransceiver(_pc.AddTransceiver(
            MediaKind.Video,
            new TransceiverInitSettings
            {
                Name = $"video_{_pc.Transceivers.Count}",
                InitialDesiredDirection = Transceiver.Direction.SendReceive,
                StreamIDs = streamIds.ToList(),
            }));
        var sender = new MrRtpSender(transceiver);
        sender.SetVideoTrack(track);
        RefreshTransceivers();
        return sender;
    }

    public IRtcRtpSender AddAudioTrack(IRtcAudioTrack track, IReadOnlyList<string> streamIds)
    {
        if (_pc == null) throw new ObjectDisposedException(nameof(MrPeerConnection));
        var transceiver = new MrTransceiver(_pc.AddTransceiver(
            MediaKind.Audio,
            new TransceiverInitSettings
            {
                Name = $"audio_{_pc.Transceivers.Count}",
                InitialDesiredDirection = Transceiver.Direction.SendReceive,
                StreamIDs = streamIds.ToList(),
            }));
        var sender = new MrRtpSender(transceiver);
        sender.SetAudioTrack(track);
        RefreshTransceivers();
        return sender;
    }

    public IRtcRtpTransceiver AddTransceiver(RtcMediaType mediaType, RtcTransceiverDirection direction)
    {
        if (_pc == null) throw new ObjectDisposedException(nameof(MrPeerConnection));
        var kind = mediaType == RtcMediaType.Video ? MediaKind.Video : MediaKind.Audio;
        var init = new TransceiverInitSettings
        {
            Name = $"{mediaType.ToString().ToLowerInvariant()}_{_pc.Transceivers.Count}",
            InitialDesiredDirection = MrAdapterHelper.ToNativeDirection(direction),
        };
        var transceiver = new MrTransceiver(_pc.AddTransceiver(kind, init));
        RefreshTransceivers();
        return transceiver;
    }

    public bool RemoveTrack(IRtcRtpSender sender) => true;

    public async Task<RtcSessionDescription> CreateOfferAsync()
    {
        if (_pc == null) throw new ObjectDisposedException(nameof(MrPeerConnection));
        _offerTcs = new TaskCompletionSource<RtcSessionDescription>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pc.CreateOffer())
            throw new InvalidOperationException("Failed to create an SDP offer.");
        return await _offerTcs.Task;
    }

    public async Task<RtcSessionDescription> CreateAnswerAsync()
    {
        if (_pc == null) throw new ObjectDisposedException(nameof(MrPeerConnection));
        _answerTcs = new TaskCompletionSource<RtcSessionDescription>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pc.CreateAnswer())
            throw new InvalidOperationException("Failed to create an SDP answer.");
        return await _answerTcs.Task;
    }

    public Task SetLocalDescriptionAsync(RtcSessionDescription desc)
        => Task.CompletedTask; // MR-WebRTC handles local SDP internally

    public async Task SetRemoteDescriptionAsync(RtcSessionDescription desc)
    {
        if (_pc == null)
            throw new ObjectDisposedException(nameof(MrPeerConnection));

        await _pc.SetRemoteDescriptionAsync(MrAdapterHelper.ToNativeSdp(desc));
        RefreshTransceivers();
        SetSignalingState(desc.Type == RtcSdpType.Offer
            ? RtcSignalingState.HaveRemoteOffer
            : RtcSignalingState.Stable);
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
        if (_closed) return;
        _closed = true;
        _pc?.Close();
        _pc?.Dispose();
        _pc = null;
        _remoteVideoTracks.Clear();
        _remoteAudioTracks.Clear();
        IceConnectionState = RtcIceConnectionState.Closed;
        ConnectionState = RtcPeerConnectionState.Closed;
        SetSignalingState(RtcSignalingState.Closed);
    }

    public void SetConfiguration(RtcConfiguration config) { /* Set at init time */ }

    public void RestartIce() => _observer.OnRenegotiationNeeded();

    public void Dispose()
    {
        Close();
    }

    private void RefreshTransceivers()
    {
        if (_pc == null)
        {
            Transceivers = [];
            return;
        }

        Transceivers = _pc.Transceivers
            .Select(t => (IRtcRtpTransceiver)new MrTransceiver(t))
            .ToList()
            .AsReadOnly();
    }

    private void SetSignalingState(RtcSignalingState state)
    {
        if (state == SignalingState) return;
        SignalingState = state;
        _observer.OnSignalingChange(state);
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

internal sealed class MrAudioSource : IRtcAudioSource, IDisposable
{
    public DeviceAudioTrackSource NativeSource { get; }

    public MrAudioSource(DeviceAudioTrackSource source)
    {
        NativeSource = source;
    }

    public void Dispose()
    {
        NativeSource.Dispose();
    }
}

// ============================================================================
// Media tracks — MR-WebRTC uses "Name" not "Id"
// ============================================================================

internal sealed class MrLocalVideoTrack : IRtcVideoTrack, IDisposable
{
    private readonly object _sinkLock = new();
    private readonly HashSet<IRtcVideoSink> _sinks = [];
    private bool _subscribed;
    private bool _disposed;

    public LocalVideoTrack NativeTrack { get; }
    public MrLocalVideoTrack(LocalVideoTrack track)
    {
        NativeTrack = track;
    }
    public string Id => NativeTrack.Name;
    public bool Enabled { get => NativeTrack.Enabled; set => NativeTrack.Enabled = value; }
    public RtcTrackState State => _disposed ? RtcTrackState.Ended : RtcTrackState.Live;
    public void AddSink(IRtcVideoSink sink)
    {
        lock (_sinkLock)
        {
            if (_disposed || !_sinks.Add(sink))
                return;
            if (!_subscribed)
            {
                NativeTrack.Argb32VideoFrameReady += HandleFrame;
                _subscribed = true;
            }
        }
    }
    public void RemoveSink(IRtcVideoSink sink)
    {
        lock (_sinkLock)
        {
            if (!_sinks.Remove(sink) || _sinks.Count != 0 || !_subscribed)
                return;
            NativeTrack.Argb32VideoFrameReady -= HandleFrame;
            _subscribed = false;
        }
    }
    public void Dispose()
    {
        lock (_sinkLock)
        {
            if (_disposed) return;
            _disposed = true;
            if (_subscribed)
            {
                NativeTrack.Argb32VideoFrameReady -= HandleFrame;
                _subscribed = false;
            }
            _sinks.Clear();
        }
        NativeTrack.Dispose();
    }

    private void HandleFrame(Argb32VideoFrame frame)
    {
        IRtcVideoSink[] sinks;
        lock (_sinkLock) sinks = [.. _sinks];
        if (sinks.Length == 0) return;

        var wrapped = new MrArgb32VideoFrame(frame);
        foreach (var sink in sinks)
        {
            try { sink.OnFrame(wrapped); }
            catch { /* A renderer must not break the WebRTC callback thread. */ }
        }
    }
}

internal sealed class MrRemoteVideoTrack : IRtcVideoTrack
{
    private readonly object _sinkLock = new();
    private readonly HashSet<IRtcVideoSink> _sinks = [];
    private bool _subscribed;

    public RemoteVideoTrack NativeTrack { get; }
    public MrRemoteVideoTrack(RemoteVideoTrack track)
    {
        NativeTrack = track;
    }
    public string Id => NativeTrack.Name;
    public bool Enabled => NativeTrack.Enabled;
    bool IRtcVideoTrack.Enabled { get => NativeTrack.Enabled; set { /* read-only */ } }
    public RtcTrackState State => RtcTrackState.Live;
    public void AddSink(IRtcVideoSink sink)
    {
        lock (_sinkLock)
        {
            if (!_sinks.Add(sink))
                return;
            if (!_subscribed)
            {
                NativeTrack.Argb32VideoFrameReady += HandleFrame;
                _subscribed = true;
            }
        }
    }
    public void RemoveSink(IRtcVideoSink sink)
    {
        lock (_sinkLock)
        {
            if (!_sinks.Remove(sink) || _sinks.Count != 0 || !_subscribed)
                return;
            NativeTrack.Argb32VideoFrameReady -= HandleFrame;
            _subscribed = false;
        }
    }

    private void HandleFrame(Argb32VideoFrame frame)
    {
        IRtcVideoSink[] sinks;
        lock (_sinkLock) sinks = [.. _sinks];
        if (sinks.Length == 0) return;

        var wrapped = new MrArgb32VideoFrame(frame);
        foreach (var sink in sinks)
        {
            try { sink.OnFrame(wrapped); }
            catch { /* A renderer must not break the WebRTC callback thread. */ }
        }
    }
}

internal sealed class MrArgb32VideoFrame : IRtcVideoFrame
{
    private readonly byte[] _data;

    public MrArgb32VideoFrame(Argb32VideoFrame frame)
    {
        Width = checked((int)frame.width);
        Height = checked((int)frame.height);
        Stride = checked(Width * 4);
        _data = new byte[checked(Stride * Height)];
        IsBlack = frame.data == IntPtr.Zero;
        if (IsBlack)
            return;

        for (var row = 0; row < Height; row++)
        {
            Marshal.Copy(
                IntPtr.Add(frame.data, checked(row * frame.stride)),
                _data,
                checked(row * Stride),
                Stride);
        }
    }

    public int Width { get; }
    public int Height { get; }
    public int Stride { get; }
    public long TimestampUs => 0;
    public bool IsBlack { get; }

    public void CopyTo(byte[] destination)
    {
        var rowBytes = Stride;
        var requiredLength = checked(rowBytes * Height);
        if (destination.Length < requiredLength)
            throw new ArgumentException("Destination buffer is too small.", nameof(destination));
        Buffer.BlockCopy(_data, 0, destination, 0, requiredLength);
    }
}

internal sealed class MrLocalAudioTrack : IRtcAudioTrack, IDisposable
{
    private bool _disposed;
    public LocalAudioTrack NativeTrack { get; }
    public string Id => NativeTrack.Name;
    public MrLocalAudioTrack(LocalAudioTrack track) => NativeTrack = track;
    public bool Enabled { get => NativeTrack.Enabled; set => NativeTrack.Enabled = value; }
    public RtcTrackState State => _disposed ? RtcTrackState.Ended : RtcTrackState.Live;
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        NativeTrack.Dispose();
    }
}

internal sealed class MrRemoteAudioTrack : IRtcAudioTrack
{
    public RemoteAudioTrack NativeTrack { get; }
    public MrRemoteAudioTrack(RemoteAudioTrack track) => NativeTrack = track;
    public string Id => NativeTrack.Name;
    public bool Enabled => NativeTrack.Enabled;
    bool IRtcAudioTrack.Enabled { get => NativeTrack.Enabled; set { /* read-only */ } }
    public RtcTrackState State => RtcTrackState.Live;
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
    public IRtcRtpSender Sender => new MrRtpSender(this);
    public IRtcVideoTrack? ReceiverVideoTrack => NativeTransceiver.RemoteVideoTrack is { } video
        ? new MrRemoteVideoTrack(video)
        : null;
    public IRtcAudioTrack? ReceiverAudioTrack => NativeTransceiver.RemoteAudioTrack is { } audio
        ? new MrRemoteAudioTrack(audio)
        : null;

    public RtcTransceiverDirection Direction
    {
        get => MrAdapterHelper.FromNativeDirection(NativeTransceiver.DesiredDirection);
        set => NativeTransceiver.DesiredDirection = MrAdapterHelper.ToNativeDirection(value);
    }
}

internal sealed class MrRtpSender : IRtcRtpSender
{
    private readonly MrTransceiver _transceiver;
    private IRtcVideoTrack? _videoTrack;
    private IRtcAudioTrack? _audioTrack;

    public MrRtpSender(MrTransceiver transceiver)
    {
        _transceiver = transceiver;
    }

    public string? TrackId => _videoTrack?.Id ?? _audioTrack?.Id;
    public IRtcVideoTrack? VideoTrack => _videoTrack;
    public IRtcAudioTrack? AudioTrack => _audioTrack;
    public void SetVideoTrack(IRtcVideoTrack? track)
    {
        _transceiver.NativeTransceiver.LocalVideoTrack = track switch
        {
            MrLocalVideoTrack local => local.NativeTrack,
            null => null,
            _ => throw new ArgumentException("Track is not a local MR-WebRTC video track.", nameof(track)),
        };
        _videoTrack = track;
    }
    public void SetAudioTrack(IRtcAudioTrack? track)
    {
        _transceiver.NativeTransceiver.LocalAudioTrack = track switch
        {
            MrLocalAudioTrack local => local.NativeTrack,
            null => null,
            _ => throw new ArgumentException("Track is not a local MR-WebRTC audio track.", nameof(track)),
        };
        _audioTrack = track;
    }
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
