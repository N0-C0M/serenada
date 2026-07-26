using Serenada.Core.Models;
using Serenada.Core.WebRtc;
using Serenada.Core.WebRtc.MixedReality;

namespace Serenada.Core.Call;

/// <summary>
/// WebRTC media engine — creates and manages peer connections, local media
/// capture, camera switching, and screen sharing.
///
/// Mirrors <c>WebRtcEngine</c> on Android and iOS.
/// </summary>
internal class WebRtcEngine : ISessionMediaEngine
{
    private readonly ISerenadaLogger? _logger;
    private readonly SerenadaConfig _config;
    private readonly LocalMediaManager _localMedia;

    private IRtcPeerConnectionFactory? _factory;
    private readonly List<IPeerConnectionSlot> _slots = [];
    private RtcConfiguration _rtcConfiguration = new();
    private bool _localMediaStarted;

    // Local media — audio via MR-WebRTC, video via LocalMediaManager
    private IRtcAudioSource? _localAudioSource;
    private IRtcAudioTrack? _localAudioTrack;
    private IRtcVideoTrack? _localVideoTrack;

    // Independent content video
    private IRtcVideoTrack? _localContentVideoTrack;

    private CameraMode _currentCameraMode = CameraMode.Selfie;

    public IRtcAudioSource? LocalAudioSource => _localAudioSource;
    public IRtcVideoSource? LocalVideoSource =>
        _localMedia.CurrentVideoSource is { } src ? new MrDeviceVideoSource(src) : null;
    public bool HasMultipleCameras =>
        _config.VideoMediaEnabled && _localMedia.HasMultipleCameras;
    public IReadOnlyList<CameraMode> AvailableCameraModes =>
        !_config.VideoMediaEnabled
            ? []
            : _config.CameraModes is { } configured
            ? configured.Where(_localMedia.AvailableModes.Contains).ToList().AsReadOnly()
            : _localMedia.AvailableModes;
    public bool CanScreenShare => _localMedia.CanScreenShare;
    public bool SupportsIndependentContentVideo => false;
    public CameraMode CurrentCameraMode => _currentCameraMode;
    public IRtcVideoTrack? LocalVideoTrack => _localVideoTrack;
    public bool HasIceServers { get; private set; }
    public event Action<IRtcVideoTrack?>? LocalVideoTrackChanged;

    public string AggregateIceConnectionState => ComputeAggregate(s => s.IceConnectionState,
        ["failed", "disconnected", "checking", "new", "connected", "completed", "closed"]);

    public string AggregatePeerConnectionState => ComputeAggregate(s => s.ConnectionState,
        ["failed", "disconnected", "connecting", "new", "connected", "closed"]);

    public WebRtcEngine(SerenadaConfig config, ISerenadaLogger? logger)
    {
        _config = config;
        _logger = logger;
        _localMedia = new LocalMediaManager(logger);
    }

    // ── Lifetime ─────────────────────────────────────────────

    public async Task StartLocalMediaAsync(bool startVideo)
    {
        if (_localMediaStarted) return;
        _factory ??= CreateFactory();

        try
        {
            _localAudioSource = _factory.CreateAudioSource();
            _localAudioTrack = _factory.CreateAudioTrack("local_audio", _localAudioSource);
            _localAudioTrack.Enabled = _config.DefaultAudioEnabled;
        }
        catch (Exception ex)
        {
            Log(SerenadaLogLevel.Warning, "WebRtcEngine",
                $"Microphone initialization failed: {ex.Message}");
        }

        if (startVideo && _config.VideoMediaEnabled)
        {
            try
            {
                await _localMedia.InitializeAsync();
                var enabledModes = _config.CameraModes is { } configuredModes
                    ? configuredModes
                        .Where(_localMedia.AvailableModes.Contains)
                        .ToList()
                    : _localMedia.AvailableModes.ToList();

                if (enabledModes.Count > 0)
                    await StartVideoTrackAsync(enabledModes[0]);
            }
            catch (Exception ex)
            {
                Log(SerenadaLogLevel.Warning, "WebRtcEngine",
                    $"Camera initialization failed: {ex.Message}");
            }
        }

        _localMediaStarted = true;
        Log(SerenadaLogLevel.Info, "WebRtcEngine", "Local media started.");
    }

    public void Release()
    {
        foreach (var slot in _slots)
            slot.Dispose();
        _slots.Clear();

        DisposeTrack(_localVideoTrack);
        DisposeTrack(_localAudioTrack);
        if (_localAudioSource is IDisposable audioSource)
            audioSource.Dispose();
        DisposeTrack(_localContentVideoTrack);

        _localVideoTrack = null;
        _localAudioTrack = null;
        _localAudioSource = null;
        _localContentVideoTrack = null;
        LocalVideoTrackChanged?.Invoke(null);

        _localMedia.Dispose();
        _factory?.Dispose();
        _factory = null;

        HasIceServers = false;
        _rtcConfiguration = new RtcConfiguration();
        _localMediaStarted = false;
    }

    // ── Media Control ────────────────────────────────────────

    public void SetAudioEnabled(bool enabled)
    {
        _localMedia.SetAudioEnabled(enabled);
        if (_localAudioTrack != null)
            _localAudioTrack.Enabled = enabled;
    }

    public void SetVideoEnabled(bool enabled)
    {
        _localMedia.SetVideoEnabled(enabled);
        if (_localVideoTrack != null)
            _localVideoTrack.Enabled = enabled;
    }

    public async Task FlipCameraAsync()
    {
        var modes = AvailableCameraModes;
        if (modes.Count <= 1) return;

        var idx = IndexOfMode(modes, _currentCameraMode);
        var next = modes[(idx + 1) % modes.Count];
        await SetCameraModeAsync(next);
    }

    public async Task SetCameraModeAsync(CameraMode mode)
    {
        if (!AvailableCameraModes.Contains(mode))
        {
            Log(SerenadaLogLevel.Warning, "WebRtcEngine",
                $"Camera mode {mode} not available. Available: {string.Join(",", _localMedia.AvailableModes)}");
            return;
        }

        Log(SerenadaLogLevel.Info, "WebRtcEngine", $"Switching camera to {mode}.");

        var wasEnabled = _localVideoTrack?.Enabled ?? _config.DefaultVideoEnabled;
        var newSource = await _localMedia.SwitchCameraAsync(mode);
        if (newSource == null) return;

        // Create a new video track from the new source
        _factory ??= CreateFactory();
        var previousTrack = _localVideoTrack;
        IRtcVideoTrack newTrack;
        try
        {
            newTrack = _factory.CreateVideoTrack("local_video",
                new MrDeviceVideoSource(newSource));
            newTrack.Enabled = wasEnabled;
        }
        catch (Exception ex)
        {
            Log(SerenadaLogLevel.Warning, "WebRtcEngine",
                $"Could not create a track for camera mode {mode}: {ex.Message}");
            return;
        }

        _localVideoTrack = newTrack;
        _currentCameraMode = _localMedia.CurrentMode;
        foreach (var slot in _slots)
            slot.SetLocalVideoTrack(_localVideoTrack);

        LocalVideoTrackChanged?.Invoke(_localVideoTrack);
        DisposeTrack(previousTrack);
    }

    public Task StartScreenShareAsync()
    {
        Log(SerenadaLogLevel.Warning, "WebRtcEngine",
            "Native screen capture is unavailable in this build.");
        return Task.CompletedTask;
    }

    public Task StopScreenShareAsync()
    {
        DisposeTrack(_localContentVideoTrack);
        _localContentVideoTrack = null;
        Log(SerenadaLogLevel.Info, "WebRtcEngine", "Screen share stopped.");
        return Task.CompletedTask;
    }

    // ── ICE Servers ──────────────────────────────────────────

    public void SetIceServers(IReadOnlyList<Signaling.IceServer> servers)
    {
        HasIceServers = servers.Count > 0;

        _rtcConfiguration = new RtcConfiguration
        {
            IceServers = servers.Select(s => new RtcIceServer
            {
                Urls = s.Urls,
                Username = s.Username,
                Password = s.Password,
            }).ToList().AsReadOnly(),
        };

        foreach (var slot in _slots)
            slot.SetIceServers(_rtcConfiguration);
    }

    // ── Slots ────────────────────────────────────────────────

    public IPeerConnectionSlot CreateSlot(
        RemoteParticipant participant, IPeerConnectionSlotCallbacks callbacks)
    {
        _factory ??= CreateFactory();

        var slot = new PeerConnectionSlot(
            factory: _factory,
            remoteCid: participant.Cid,
            supportsIndependentContentVideo: participant.SupportsIndependentContentVideo
                && SupportsIndependentContentVideo,
            callbacks: callbacks,
            localAudioTrack: _localAudioTrack,
            localVideoTrack: _localVideoTrack,
            localContentVideoTrack: _localContentVideoTrack,
            videoMediaEnabled: _config.VideoMediaEnabled,
            rtcConfiguration: _rtcConfiguration,
            logger: _logger);

        _slots.Add(slot);
        return slot;
    }

    public void RemoveSlot(IPeerConnectionSlot slot)
    {
        _slots.Remove(slot);
        slot.Dispose();
    }

    public void Dispose()
    {
        Release();
    }

    // ── Internals ────────────────────────────────────────────

    private async Task StartVideoTrackAsync(CameraMode mode)
    {
        var source = await _localMedia.StartVideoCaptureAsync(mode);
        if (source == null) return;

        _currentCameraMode = mode;
        _factory ??= CreateFactory();
        _localVideoTrack = _factory.CreateVideoTrack("local_video",
            new MrDeviceVideoSource(source));
        _localVideoTrack.Enabled = _config.DefaultVideoEnabled;
        LocalVideoTrackChanged?.Invoke(_localVideoTrack);
    }

    private IRtcPeerConnectionFactory CreateFactory()
    {
        var platform = new MrWebRtcPlatform();
        platform.Initialize();
        return platform.CreateFactory();
    }

    private string ComputeAggregate(
        Func<IPeerConnectionSlot, string> selector, string[] priorityOrder)
    {
        if (_slots.Count == 0) return "closed";

        foreach (var state in priorityOrder)
        {
            if (_slots.Any(s => selector(s) == state))
                return state;
        }
        return "closed";
    }

    private static int IndexOfMode(IReadOnlyList<CameraMode> list, CameraMode item)
    {
        for (var i = 0; i < list.Count; i++)
            if (list[i] == item) return i;
        return -1;
    }

    private void Log(SerenadaLogLevel level, string tag, string message)
    {
        _logger?.Log(level, tag, message);
    }

    private static void DisposeTrack(object? track)
    {
        if (track is IDisposable disposable)
            disposable.Dispose();
    }
}

/// <summary>
/// Extension methods for setting ICE servers on a slot.
/// </summary>
internal static class PeerConnectionSlotExtensions
{
    public static void SetIceServers(this IPeerConnectionSlot slot,
        RtcConfiguration config)
    {
        if (slot is PeerConnectionSlot concreteSlot)
        {
            concreteSlot.SetIceServers(config);
        }
    }
}
