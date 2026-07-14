// ============================================================================
// RtcVideoSinkAdapter — bridges native video frames to managed sinks.
// ============================================================================

#include "RtcVideoSinkAdapter.h"

// libwebrtc includes
#include "api/video/video_frame.h"
#include "api/video/video_sink_interface.h"

namespace Serenada {
namespace WebRtc {
namespace Native {

// ========================================================================
// RtcVideoFrameBridge
// ========================================================================

RtcVideoFrameBridge::RtcVideoFrameBridge(const webrtc::VideoFrame& frame)
    : _width(frame.width())
    , _height(frame.height())
    , _timestampUs(frame.timestamp_us())
{
}

int RtcVideoFrameBridge::Width::get()        { return _width; }
int RtcVideoFrameBridge::Height::get()       { return _height; }
long RtcVideoFrameBridge::TimestampUs::get()  { return _timestampUs; }

bool RtcVideoFrameBridge::IsBlack::get()
{
    // Detect synthetic black frames (remote video turned off).
    // For now, always return false — full black-frame analysis
    // is added in Phase 3 with the RemoteBlackFrameAnalyzer.
    return false;
}

// ========================================================================
// ManagedVideoSinkAdapter
// ========================================================================

ManagedVideoSinkAdapter::ManagedVideoSinkAdapter(
    gcroot<IRtcVideoSink^> managedSink)
    : _sink(managedSink)
{
}

void ManagedVideoSinkAdapter::OnFrame(const webrtc::VideoFrame& frame)
{
    auto managedFrame = gcnew RtcVideoFrameBridge(frame);
    _sink->OnFrame(managedFrame);
}

} // Native
} // WebRtc
} // Serenada
