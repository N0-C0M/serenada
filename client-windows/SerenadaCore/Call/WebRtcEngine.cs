using Serenada.Core.Models;
using Serenada.Core.WebRtc;
using Serenada.Core.WebRtc.MixedReality;

namespace Serenada.Core.Call;

/// <summary>
/// WebRTC media engine — creates and manages peer connections, local media
/// capture, and screen sharing.
///
/// Mirrors <c>WebRtcEngine</c> on Android and iOS.
/// </summary>
internal class WebRtcEngine : ISessionMediaEngine
{
    private readonly ISerenadaLogger? _logger;
    private readonly SerenadaConfig _config;

    private IRtcPeerConnectionFactory? _factory;
    private readonly List<IPeerConnectionSlot> _slots = [];

    // Local media
    private IRtcVideoSource? _localVideoSource;
    private IRtcAudioSource? _localAudioSource;
    private IRtcVideoTrack? _localVideoTrack;
    private IRtcAudioTrack? _localAudioTrack;

    // Independent content video
    private IRtcVideoSource? _localContentVideoSource;
    private IRtcVideoTrack? _localContentVideoTrack;

    private int _cameraCount;

    public IRtcAudioSource? LocalAudioSource => _localAudioSource;
    public IRtcVideoSource? LocalVideoSource => _localVideoSource;
    public bool HasMultipleCameras => _cameraCount > 1;
    public bool CanScreenShare => true; // Windows always supports screen capture
    public bool HasIceServers { get; private set; }

    public string AggregateIceConnectionState => ComputeAggregate(s => s.IceConnectionState,
        ["failed", "disconnected", "checking", "new", "connected", "completed", "closed"]);

    public string AggregatePeerConnectionState => ComputeAggregate(s => s.ConnectionState,
        ["failed", "disconnected", "connecting", "new", "connected", "closed"]);

    public WebRtcEngine(SerenadaConfig config, ISerenadaLogger? logger)
    {
        _config = config;
        _logger = logger;
    }

    // ── Lifetime ─────────────────────────────────────────────

    public async Task StartLocalMediaAsync(bool startVideo)
    {
        // Create the factory on first use
        _factory ??= CreateFactory();

        // Audio source + track (always created)
        _localAudioSource = _factory.CreateAudioSource();
        _localAudioTrack = _factory.CreateAudioTrack("local_audio", _localAudioSource);

        if (startVideo && _config.VideoMediaEnabled)
        {
            _localVideoSource = _factory.CreateVideoSource(isScreencast: false);
            _localVideoTrack = _factory.CreateVideoTrack("local_video", _localVideoSource);
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
        _localVideoSource = null;
        _localAudioSource = null;
        _localContentVideoTrack = null;
        _localContentVideoSource = null;

        _factory?.Dispose();
        _factory = null;

        HasIceServers = false;
    }

    // ── Media Control ────────────────────────────────────────

    public void SetAudioEnabled(bool enabled)
    {
        if (_localAudioTrack != null)
            _localAudioTrack.Enabled = enabled;
    }

    public void SetVideoEnabled(bool enabled)
    {
        if (_localVideoTrack != null)
            _localVideoTrack.Enabled = enabled;
    }

    public Task FlipCameraAsync()
    {
        // Camera switching will be handled by CameraManager in Phase 3
        Log(SerenadaLogLevel.Debug, "WebRtcEngine", "Flip camera requested.");
        return Task.CompletedTask;
    }

    public Task SetCameraModeAsync(CameraMode mode)
    {
        Log(SerenadaLogLevel.Debug, "WebRtcEngine", $"Set camera mode: {mode}.");
        return Task.CompletedTask;
    }

    public Task StartScreenShareAsync()
    {
        if (!_config.EnableIndependentContentVideo)
        {
            Log(SerenadaLogLevel.Warning, "WebRtcEngine",
                "Screen share requires enableIndependentContentVideo=true.");
            return Task.CompletedTask;
        }

        _factory ??= CreateFactory();
        _localContentVideoSource = _factory.CreateVideoSource(isScreencast: true);
        _localContentVideoTrack = _factory.CreateVideoTrack("local_content", _localContentVideoSource);

        // Attach content track to slots that support independent content
        Log(SerenadaLogLevel.Info, "WebRtcEngine", "Screen share started.");
        return Task.CompletedTask;
    }

    public Task StopScreenShareAsync()
    {
        _localContentVideoTrack = null;
        _localContentVideoSource = null;
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
