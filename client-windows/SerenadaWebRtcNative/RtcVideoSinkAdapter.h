#pragma once

// ============================================================================
// Video sink adapter — bridges libwebrtc's rtc::VideoSinkInterface<webrtc::VideoFrame>
// to the managed IRtcVideoSink interface.
// ============================================================================

#include <memory>

namespace webrtc
{
    class VideoFrame;
    template <typename T> class VideoSinkInterface;
}

namespace Serenada {
namespace WebRtc {
namespace Native {

using namespace Serenada::Core::WebRtc;

/// <summary>
/// Native-to-managed video frame adapter.
/// Wraps webrtc::VideoFrame and exposes it as IRtcVideoFrame.
/// </summary>
ref class RtcVideoFrameBridge : public IRtcVideoFrame
{
public:
    explicit RtcVideoFrameBridge(const webrtc::VideoFrame& frame);

    virtual property int Width { int get(); }
    virtual property int Height { int get(); }
    virtual property long TimestampUs { long get(); }
    virtual property bool IsBlack { bool get(); }

private:
    int _width, _height;
    long _timestampUs;
};

/// <summary>
/// Native video sink that forwards to a managed IRtcVideoSink.
/// Implements rtc::VideoSinkInterface<webrtc::VideoFrame> and
/// calls back into the managed sink on each frame.
/// </summary>
class ManagedVideoSinkAdapter : public rtc::VideoSinkInterface<webrtc::VideoFrame>
{
public:
    explicit ManagedVideoSinkAdapter(gcroot<IRtcVideoSink^> managedSink);

    void OnFrame(const webrtc::VideoFrame& frame) override;

private:
    gcroot<IRtcVideoSink^> _sink;
};

} // Native
} // WebRtc
} // Serenada
