using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Serenada.Core.WebRtc;

namespace Serenada.CallUI;

/// <summary>
/// Copies native WebRTC ARGB32 frames on the callback thread and presents only
/// the newest pending frame on the WinUI dispatcher.
/// </summary>
internal sealed class VideoFramePresenter : IRtcVideoSink, IDisposable
{
    private readonly Image _image;
    private readonly DispatcherQueue _dispatcher;
    private readonly object _frameLock = new();
    private readonly Action<bool>? _onFrameAvailabilityChanged;
    private readonly Action<string>? _diagnosticLog;

    private IRtcVideoTrack? _track;
    private PendingFrame? _pendingFrame;
    private WriteableBitmap? _bitmap;
    private int _renderQueued;
    private int _firstFrameReceived;
    private int _firstFramePresented;
    private int _renderErrorReported;
    private bool _disposed;

    public VideoFramePresenter(
        Image image,
        Action<bool>? onFrameAvailabilityChanged = null,
        Action<string>? diagnosticLog = null)
    {
        _image = image;
        _dispatcher = image.DispatcherQueue;
        _onFrameAvailabilityChanged = onFrameAvailabilityChanged;
        _diagnosticLog = diagnosticLog;
    }

    public void SetTrack(IRtcVideoTrack? track)
    {
        if (ReferenceEquals(_track, track))
            return;

        _track?.RemoveSink(this);
        _track = track;
        Interlocked.Exchange(ref _firstFrameReceived, 0);
        Interlocked.Exchange(ref _firstFramePresented, 0);
        Interlocked.Exchange(ref _renderErrorReported, 0);
        _bitmap = null;
        _image.Source = null;
        _onFrameAvailabilityChanged?.Invoke(false);
        _image.Visibility = track == null
            ? Visibility.Collapsed
            : Visibility.Visible;
        track?.AddSink(this);
    }

    public void OnFrame(IRtcVideoFrame frame)
    {
        if (_disposed || frame.Width <= 0 || frame.Height <= 0)
            return;

        var data = new byte[checked(frame.Stride * frame.Height)];
        frame.CopyTo(data);
        EnsureOpaqueBgra(data, frame.Width, frame.Height, frame.Stride);
        if (Interlocked.Exchange(ref _firstFrameReceived, 1) == 0)
        {
            _diagnosticLog?.Invoke(
                $"first frame received: {frame.Width}x{frame.Height}, " +
                $"stride={frame.Stride}, black={frame.IsBlack}");
        }
        lock (_frameLock)
        {
            _pendingFrame = new PendingFrame(
                frame.Width,
                frame.Height,
                frame.Stride,
                data);
        }

        QueueRender();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _track?.RemoveSink(this);
        _track = null;
        lock (_frameLock) _pendingFrame = null;
        _bitmap = null;
        _image.Source = null;
    }

    private void QueueRender()
    {
        if (Interlocked.Exchange(ref _renderQueued, 1) != 0)
            return;

        if (!_dispatcher.TryEnqueue(RenderPendingFrame))
            Interlocked.Exchange(ref _renderQueued, 0);
    }

    private void RenderPendingFrame()
    {
        if (_disposed)
        {
            Interlocked.Exchange(ref _renderQueued, 0);
            return;
        }

        PendingFrame? frame;
        lock (_frameLock)
        {
            frame = _pendingFrame;
            _pendingFrame = null;
        }

        try
        {
            if (frame != null)
            {
                if (_bitmap == null ||
                    _bitmap.PixelWidth != frame.Width ||
                    _bitmap.PixelHeight != frame.Height)
                {
                    _bitmap = new WriteableBitmap(frame.Width, frame.Height);
                    _image.Source = _bitmap;
                }

                using var stream = _bitmap.PixelBuffer.AsStream();
                stream.Position = 0;
                stream.Write(frame.Data, 0, frame.Data.Length);
                _bitmap.Invalidate();
                _image.Visibility = Visibility.Visible;
                if (Interlocked.Exchange(ref _firstFramePresented, 1) == 0)
                {
                    _onFrameAvailabilityChanged?.Invoke(true);
                    _diagnosticLog?.Invoke(
                        $"first frame presented: {frame.Width}x{frame.Height}");
                }
            }
        }
        catch (Exception ex)
        {
            // A transient bitmap failure must not stop later video frames.
            if (Interlocked.Exchange(ref _renderErrorReported, 1) == 0)
            {
                _diagnosticLog?.Invoke(
                    $"frame presentation failed: {ex.Message}");
            }
        }
        finally
        {
            // Never leave the presenter permanently stuck if WinUI rejects a
            // transient bitmap update while the control is unloading.
            Interlocked.Exchange(ref _renderQueued, 0);
            lock (_frameLock)
            {
                if (_pendingFrame != null)
                    QueueRender();
            }
        }
    }

    private static void EnsureOpaqueBgra(
        byte[] data,
        int width,
        int height,
        int stride)
    {
        if (data.Length < 4 || data[3] == byte.MaxValue)
            return;

        var rowBytes = checked(width * 4);
        for (var row = 0; row < height; row++)
        {
            var rowStart = checked(row * stride);
            var rowEnd = checked(rowStart + rowBytes);
            for (var alpha = rowStart + 3; alpha < rowEnd; alpha += 4)
                data[alpha] = byte.MaxValue;
        }
    }

    private sealed record PendingFrame(
        int Width,
        int Height,
        int Stride,
        byte[] Data);
}
