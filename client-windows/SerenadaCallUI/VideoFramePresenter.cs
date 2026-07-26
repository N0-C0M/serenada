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

    private IRtcVideoTrack? _track;
    private PendingFrame? _pendingFrame;
    private WriteableBitmap? _bitmap;
    private int _renderQueued;
    private bool _disposed;

    public VideoFramePresenter(Image image)
    {
        _image = image;
        _dispatcher = image.DispatcherQueue;
    }

    public void SetTrack(IRtcVideoTrack? track)
    {
        if (ReferenceEquals(_track, track))
            return;

        _track?.RemoveSink(this);
        _track = track;
        _bitmap = null;
        _image.Source = null;
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
            }
        }
        catch
        {
            // A transient bitmap failure must not stop later video frames.
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

    private sealed record PendingFrame(
        int Width,
        int Height,
        int Stride,
        byte[] Data);
}
