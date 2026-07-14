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

    // Local media — audio via MR-WebRTC, video via LocalMediaManager
    private IRtcAudioSource? _localAudioSource;
    private IRtcAudioTrack? _localAudioTrack;
    private IRtcVideoTrack? _localVideoTrack;

    // Independent content video
    private MrExternalVideoSourceAdapter? _screenShareAdapter;
    private IRtcVideoTrack? _localContentVideoTrack;

    private CameraMode _currentCameraMode = CameraMode.Selfie;

    public IRtcAudioSource? LocalAudioSource => _localAudioSource;
    public IRtcVideoSource? LocalVideoSource =>
        _localMedia.CurrentVideoSource is { } src ? new MrDeviceVideoSource(src) : null;
    public bool HasMultipleCameras => _localMedia.HasMultipleCameras;
    public bool CanScreenShare => _localMedia.CanScreenShare;
    public bool HasIceServers { get; private set; }

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
        _factory ??= CreateFactory();

        // Audio source + track
        _localAudioSource = _factory.CreateAudioSource();
        _localAudioTrack = _factory.CreateAudioTrack("local_audio", _localAudioSource);

        if (startVideo && _config.VideoMediaEnabled)
        {
            // Use the first available camera mode from config
            var preferredMode = _config.CameraModes?.FirstOrDefault()
                ?? _localMedia.AvailableModes.FirstOrDefault();

            await StartVideoTrackAsync(preferredMode);
        }

        Log(SerenadaLogLevel.Info, "WebRtcEngine", "Local media started.");
    }

    public void Release()
    {
        foreach (var slot in _slots)
            slot.Dispose();
        _slots.Clear();

        _localVideoTrack = null;
        _localAudioTrack = null;
        _localAudioSource = null;
        _localContentVideoTrack = null;
        _screenShareAdapter = null;

        _localMedia.Dispose();
        _factory?.Dispose();
        _factory = null;

        HasIceServers = false;
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
        var modes = _localMedia.AvailableModes;
        if (modes.Count <= 1) return;

        var idx = IndexOfMode(modes, _currentCameraMode);
        var next = modes[(idx + 1) % modes.Count];
        await SetCameraModeAsync(next);
    }

    public async Task SetCameraModeAsync(CameraMode mode)
    {
        if (!_localMedia.AvailableModes.Contains(mode))
        {
            Log(SerenadaLogLevel.Warning, "WebRtcEngine",
                $"Camera mode {mode} not available. Available: {string.Join(",", _localMedia.AvailableModes)}");
            return;
        }

        Log(SerenadaLogLevel.Info, "WebRtcEngine", $"Switching camera to {mode}.");

        var newSource = await _localMedia.SwitchCameraAsync(mode);
        if (newSource == null) return;

        _currentCameraMode = mode;

        // Create a new video track from the new source
        _factory ??= CreateFactory();
        _localVideoTrack = _factory.CreateVideoTrack("local_video",
            new MrDeviceVideoSource(newSource));

        // Update all existing slots with the new track
        // (in Unified Plan, we'd replace the track on the sender;
        // simpler: recreate slots — handled by session on next negotiation)
    }

    public async Task StartScreenShareAsync()
    {
        if (!_config.EnableIndependentContentVideo)
        {
            Log(SerenadaLogLevel.Warning, "WebRtcEngine",
                "Screen share requires enableIndependentContentVideo=true.");
            return;
        }

        _screenShareAdapter = new MrExternalVideoSourceAdapter();
        var track = _screenShareAdapter.CreateTrack("local_content");

        _factory ??= CreateFactory();
        _localContentVideoTrack = new MrLocalVideoTrack(track);

        Log(SerenadaLogLevel.Info, "WebRtcEngine", "Screen share started.");
    }

    public Task StopScreenShareAsync()
    {
        _localContentVideoTrack = null;
        _screenShareAdapter = null;
        Log(SerenadaLogLevel.Info, "WebRtcEngine", "Screen share stopped.");
        return Task.CompletedTask;
    }

    // ── ICE Servers ──────────────────────────────────────────

    public void SetIceServers(IReadOnlyList<Signaling.IceServer> servers)
    {
        HasIceServers = servers.Count > 0;

        var config = new RtcConfiguration
        {
            IceServers = servers.Select(s => new RtcIceServer
            {
                Urls = s.Urls,
                Username = s.Username,
                Password = s.Password,
            }).ToList().AsReadOnly(),
        };

        foreach (var slot in _slots)
            slot.SetIceServers(config);
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
                && _config.EnableIndependentContentVideo,
            callbacks: callbacks,
            localAudioTrack: _localAudioTrack,
            localVideoTrack: _localVideoTrack,
            localContentVideoTrack: _localContentVideoTrack,
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
