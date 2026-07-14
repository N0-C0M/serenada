using Windows.Devices.Enumeration;
using Windows.Media.Capture;
using Windows.Media.MediaProperties;
using Microsoft.MixedReality.WebRTC;
using Serenada.Core.Models;
using Serenada.Core.WebRtc;

namespace Serenada.Core.Call;

/// <summary>
/// Manages local media capture — camera enumeration, mode switching,
/// screen sharing, and microphone access. Bridges Windows.Media.Capture
/// and Windows.Graphics.Capture to MixedReality.WebRTC video sources.
/// </summary>
internal class LocalMediaManager : IDisposable
{
    private readonly ISerenadaLogger? _logger;
    private MediaCapture? _mediaCapture;
    private DeviceInformationCollection? _videoDevices;

    // Current state
    private CameraMode _currentMode = CameraMode.Selfie;
    private DeviceVideoTrackSource? _currentVideoSource;
    private LocalVideoTrack? _currentVideoTrack;
    private bool _videoEnabled = true;
    private bool _audioEnabled = true;
    private bool _disposed;

    // Camera info
    private DeviceInformation? _frontCamera;
    private DeviceInformation? _backCamera;
    private DeviceInformation? _firstAdditionalCamera;
    private bool _initialized;

    public DeviceVideoTrackSource? CurrentVideoSource => _currentVideoSource;
    public LocalVideoTrack? CurrentVideoTrack => _currentVideoTrack;
    public IReadOnlyList<CameraMode> AvailableModes { get; private set; } = [CameraMode.Selfie];
    public bool HasMultipleCameras => AvailableModes.Count > 1;
    public bool CanScreenShare => true;

    public LocalMediaManager(ISerenadaLogger? logger)
    {
        _logger = logger;
    }

    // ── Initialization ───────────────────────────────────────

    /// <summary>
    /// Enumerate video capture devices to determine available camera modes.
    /// </summary>
    public async Task InitializeAsync()
    {
        if (_initialized) return;

        try
        {
            _videoDevices = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);

            foreach (var device in _videoDevices)
            {
                if (device.EnclosureLocation?.Panel == Panel.Front)
                    _frontCamera ??= device;
                else if (device.EnclosureLocation?.Panel == Panel.Back)
                    _backCamera ??= device;
                else
                    _firstAdditionalCamera ??= device;
            }

            // Determine available modes
            var modes = new List<CameraMode>();
            if (_frontCamera != null) modes.Add(CameraMode.Selfie);
            if (_backCamera != null) modes.Add(CameraMode.World);
            if (_frontCamera != null && _firstAdditionalCamera != null)
                modes.Add(CameraMode.Composite);

            AvailableModes = modes.Count > 0 ? modes : [CameraMode.Selfie];

            Log(SerenadaLogLevel.Info, "Media",
                $"Cameras found: front={_frontCamera?.Name}, back={_backCamera?.Name}, " +
                $"modes={string.Join(",", AvailableModes)}");

            _initialized = true;
        }
        catch (Exception ex)
        {
            Log(SerenadaLogLevel.Warning, "Media", $"Camera enumeration failed: {ex.Message}");
            AvailableModes = [];
        }
    }

    // ── Video ────────────────────────────────────────────────

    /// <summary>
    /// Start video capture using MR-WebRTC's device video track source.
    /// </summary>
    public async Task<DeviceVideoTrackSource?> StartVideoCaptureAsync(CameraMode mode)
    {
        await InitializeAsync();

        _currentMode = mode;

        var targetDevice = mode switch
        {
            CameraMode.Selfie => _frontCamera,
            CameraMode.World => _backCamera ?? _frontCamera,
            CameraMode.Composite => _frontCamera, // Main cam for composite
            _ => _frontCamera ?? _videoDevices?.FirstOrDefault(),
        };

        if (targetDevice == null)
        {
            Log(SerenadaLogLevel.Warning, "Media", "No camera available for video capture.");
            return null;
        }

        // Stop any existing capture
        await StopVideoCaptureAsync();

        try
        {
            var config = new LocalVideoDeviceInitConfig
            {
                videoDevice = new VideoCaptureDevice
                {
                    id = targetDevice.Id,
                    name = targetDevice.Name,
                },
            };

            _currentVideoSource = await DeviceVideoTrackSource.CreateAsync(config);
            Log(SerenadaLogLevel.Info, "Media",
                $"Video capture started: {targetDevice.Name} (mode={mode}).");
        }
        catch (Exception ex)
        {
            Log(SerenadaLogLevel.Error, "Media", $"Video capture failed: {ex.Message}");
            return null;
        }

        return _currentVideoSource;
    }

    /// <summary>
    /// Stop video capture.
    /// </summary>
    public Task StopVideoCaptureAsync()
    {
        _currentVideoTrack?.Dispose();
        _currentVideoTrack = null;

        _currentVideoSource?.Dispose();
        _currentVideoSource = null;

        return Task.CompletedTask;
    }

    /// <summary>
    /// Switch to a different camera mode.
    /// </summary>
    public async Task<DeviceVideoTrackSource?> SwitchCameraAsync(CameraMode mode)
    {
        var source = await StartVideoCaptureAsync(mode);
        _currentMode = mode;
        return source;
    }

    public void SetVideoEnabled(bool enabled)
    {
        _videoEnabled = enabled;
        if (_currentVideoTrack != null)
            _currentVideoTrack.Enabled = enabled;
    }

    // ── Audio ────────────────────────────────────────────────

    public void SetAudioEnabled(bool enabled)
    {
        _audioEnabled = enabled;
    }

    // ── Screen Share ─────────────────────────────────────────

    /// <summary>
    /// Start screen sharing. Uses Windows.Graphics.Capture to capture
    /// the screen/window and feeds frames into an external video track source.
    /// Placeholder — full implementation requires a WinUI window handle.
    /// </summary>
    public Task<MrExternalVideoSourceAdapter?> StartScreenShareAsync()
    {
        Log(SerenadaLogLevel.Info, "Media", "Screen share requested.");
        // Full implementation in follow-up: Windows.Graphics.Capture +
        // GraphicsCapturePicker → ExternalVideoTrackSource frame feeding.
        return Task.FromResult<MrExternalVideoSourceAdapter?>(null);
    }

    /// <summary>
    /// Stop screen sharing.
    /// </summary>
    public Task StopScreenShareAsync()
    {
        return Task.CompletedTask;
    }

    // ── Cleanup ──────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _currentVideoTrack?.Dispose();
        _currentVideoSource?.Dispose();
        _mediaCapture?.Dispose();
        _mediaCapture = null;
    }

    private void Log(SerenadaLogLevel level, string tag, string message)
    {
        _logger?.Log(level, tag, message);
    }
}

/// <summary>
/// Adapter for external screen-share video source.
/// Placeholder for Windows.Graphics.Capture integration.
/// </summary>
internal class MrExternalVideoSourceAdapter
{
    public ExternalVideoTrackSource? Source { get; private set; }

    public MrExternalVideoSourceAdapter()
    {
        Source = ExternalVideoTrackSource.CreateFromArgb32Callback(OnFrameRequested);
    }

    private void OnFrameRequested(in FrameRequest request)
    {
        // Frames are submitted by the screen capture pipeline.
        // Full implementation: hook GraphicsCaptureSession.FrameArrived
        // and call Source.SubmitArgb32Frame / CompleteFrameRequest.
    }

    public LocalVideoTrack CreateTrack(string id)
    {
        if (Source == null)
            throw new InvalidOperationException("Source not initialized.");
        return LocalVideoTrack.CreateFromSource(Source,
            new LocalVideoTrackInitConfig { trackName = id });
    }
}
