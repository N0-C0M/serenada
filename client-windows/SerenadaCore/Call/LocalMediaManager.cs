using Windows.Devices.Enumeration;
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
    private IReadOnlyList<VideoCaptureDevice> _videoDevices = [];

    // Current state
    private CameraMode _currentMode = CameraMode.Selfie;
    private DeviceVideoTrackSource? _currentVideoSource;
    private LocalVideoTrack? _currentVideoTrack;
    private bool _videoEnabled = true;
    private bool _audioEnabled = true;
    private bool _disposed;

    // Camera info
    private VideoCaptureDevice? _frontCamera;
    private VideoCaptureDevice? _backCamera;
    private bool _initialized;

    public DeviceVideoTrackSource? CurrentVideoSource => _currentVideoSource;
    public LocalVideoTrack? CurrentVideoTrack => _currentVideoTrack;
    public CameraMode CurrentMode => _currentMode;
    public IReadOnlyList<CameraMode> AvailableModes { get; private set; } = [CameraMode.Selfie];
    public bool HasMultipleCameras => AvailableModes.Count > 1;
    public bool CanScreenShare => false;

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
            _videoDevices = (await DeviceVideoTrackSource.GetCaptureDevicesAsync())
                .ToList()
                .AsReadOnly();
            IReadOnlyList<DeviceInformation> systemDevices;
            try
            {
                systemDevices = (await DeviceInformation.FindAllAsync(
                    DeviceClass.VideoCapture))
                    .ToList()
                    .AsReadOnly();
            }
            catch
            {
                systemDevices = [];
            }

            var unpositionedCameras = new List<VideoCaptureDevice>();
            foreach (var device in _videoDevices)
            {
                var systemDevice = systemDevices.FirstOrDefault(candidate =>
                    string.Equals(candidate.Id, device.id,
                        StringComparison.OrdinalIgnoreCase))
                    ?? systemDevices.FirstOrDefault(candidate =>
                        string.Equals(candidate.Name, device.name,
                            StringComparison.OrdinalIgnoreCase));
                if (systemDevice?.EnclosureLocation?.Panel == Panel.Front)
                    _frontCamera ??= device;
                else if (systemDevice?.EnclosureLocation?.Panel == Panel.Back)
                    _backCamera ??= device;
                else
                    unpositionedCameras.Add(device);
            }

            // External USB webcams generally have no enclosure panel metadata.
            // Treat the first as selfie and a second distinct device as world.
            if (_frontCamera == null)
            {
                _frontCamera = unpositionedCameras.Count > 0
                    ? unpositionedCameras[0]
                    : _videoDevices.Count > 0
                        ? _videoDevices[0]
                        : null;
            }
            if (_backCamera == null && _frontCamera is { } frontCamera)
            {
                _backCamera = FirstOrNull(
                    unpositionedCameras,
                    device => device.id != frontCamera.id);
            }

            // Determine available modes
            var modes = new List<CameraMode>();
            if (_frontCamera != null) modes.Add(CameraMode.Selfie);
            if (_backCamera is { } backCamera &&
                _frontCamera is { } resolvedFrontCamera &&
                backCamera.id != resolvedFrontCamera.id)
                modes.Add(CameraMode.World);

            AvailableModes = modes.Count > 0 ? modes : [CameraMode.Selfie];

            Log(SerenadaLogLevel.Info, "Media",
                $"Cameras found: front={_frontCamera?.name}, back={_backCamera?.name}, " +
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

        var previousMode = _currentMode;
        var previousDevice = ResolveCamera(previousMode);
        var hadActiveSource = _currentVideoSource != null;
        var targetDevice = ResolveCamera(mode);

        if (targetDevice is not { } resolvedTargetDevice)
        {
            Log(SerenadaLogLevel.Warning, "Media", "No camera available for video capture.");
            return null;
        }

        // Stop any existing capture
        await StopVideoCaptureAsync();

        try
        {
            _currentVideoSource = await CreateVideoSourceAsync(resolvedTargetDevice);
            _currentMode = mode;
            Log(SerenadaLogLevel.Info, "Media",
                $"Video capture started: {resolvedTargetDevice.name} (mode={mode}).");
        }
        catch (Exception ex)
        {
            Log(SerenadaLogLevel.Error, "Media", $"Video capture failed: {ex.Message}");
            if (!hadActiveSource || previousDevice is not { } resolvedPreviousDevice)
                return null;

            try
            {
                _currentVideoSource = await CreateVideoSourceAsync(resolvedPreviousDevice);
                _currentMode = previousMode;
                Log(SerenadaLogLevel.Warning, "Media",
                    $"Restored previous camera: {resolvedPreviousDevice.name}.");
            }
            catch (Exception restoreError)
            {
                Log(SerenadaLogLevel.Error, "Media",
                    $"Could not restore the previous camera: {restoreError.Message}");
                return null;
            }
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
        return await StartVideoCaptureAsync(mode);
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
    }

    private void Log(SerenadaLogLevel level, string tag, string message)
    {
        _logger?.Log(level, tag, message);
    }

    private VideoCaptureDevice? ResolveCamera(CameraMode mode)
    {
        return mode switch
        {
            CameraMode.Selfie => _frontCamera,
            CameraMode.World => _backCamera ?? _frontCamera,
            CameraMode.Composite => null,
            _ => _frontCamera ?? _videoDevices.FirstOrDefault(),
        };
    }

    private static Task<DeviceVideoTrackSource> CreateVideoSourceAsync(
        VideoCaptureDevice device)
    {
        return DeviceVideoTrackSource.CreateAsync(new LocalVideoDeviceInitConfig
        {
            videoDevice = device,
        });
    }

    private static VideoCaptureDevice? FirstOrNull(
        IEnumerable<VideoCaptureDevice> devices,
        Func<VideoCaptureDevice, bool> predicate)
    {
        foreach (var device in devices)
        {
            if (predicate(device))
                return device;
        }
        return null;
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
