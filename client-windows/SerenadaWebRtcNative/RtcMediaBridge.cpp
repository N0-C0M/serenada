// ============================================================================
// RtcMediaBridge — wraps webrtc media tracks and streams.
// ============================================================================

#include "RtcMediaBridge.h"
#include "RtcVideoSinkAdapter.h"

// libwebrtc includes
#include "api/media_stream_interface.h"

using namespace msclr::interop;

namespace Serenada {
namespace WebRtc {
namespace Native {

// ========================================================================
// RtcVideoTrackBridge
// ========================================================================

RtcVideoTrackBridge::RtcVideoTrackBridge(
    rtc::scoped_refptr<webrtc::VideoTrackInterface> native)
    : _native(native), _disposed(false)
{
}

RtcVideoTrackBridge::~RtcVideoTrackBridge()
{
    this->!RtcVideoTrackBridge();
}

RtcVideoTrackBridge::!RtcVideoTrackBridge()
{
    if (!_disposed)
    {
        _native = nullptr;
        _disposed = true;
    }
}

String^ RtcVideoTrackBridge::Id::get()
{
    return marshal_as<String^>(_native->id());
}

bool RtcVideoTrackBridge::Enabled::get()
{
    return _native->enabled();
}

void RtcVideoTrackBridge::Enabled::set(bool value)
{
    _native->set_enabled(value);
}

RtcTrackState RtcVideoTrackBridge::State::get()
{
    return _native->state() == webrtc::MediaStreamTrackInterface::kLive
        ? RtcTrackState::Live : RtcTrackState::Ended;
}

void RtcVideoTrackBridge::AddSink(IRtcVideoSink^ sink)
{
    // Create a native adapter that forwards frames to the managed sink
    auto adapter = std::make_unique<ManagedVideoSinkAdapter>(sink);
    _native->AddOrUpdateSink(adapter.release(), rtc::VideoSinkWants());
}

void RtcVideoTrackBridge::RemoveSink(IRtcVideoSink^ sink)
{
    // Removal by managed sink identity — the native adapter is identified
    // by its pointer. In practice we track adapters in a dictionary keyed
    // on the managed sink. For simplicity, remove by iterating.
    _native->RemoveSink(nullptr); // Stub — proper impl tracks adapters per sink
}

// ========================================================================
// RtcAudioTrackBridge
// ========================================================================

RtcAudioTrackBridge::RtcAudioTrackBridge(
    rtc::scoped_refptr<webrtc::AudioTrackInterface> native)
    : _native(native), _disposed(false)
{
}

RtcAudioTrackBridge::~RtcAudioTrackBridge()
{
    this->!RtcAudioTrackBridge();
}

RtcAudioTrackBridge::!RtcAudioTrackBridge()
{
    if (!_disposed)
    {
        _native = nullptr;
        _disposed = true;
    }
}

String^ RtcAudioTrackBridge::Id::get()
{
    return marshal_as<String^>(_native->id());
}

bool RtcAudioTrackBridge::Enabled::get()
{
    return _native->enabled();
}

void RtcAudioTrackBridge::Enabled::set(bool value)
{
    _native->set_enabled(value);
}

RtcTrackState RtcAudioTrackBridge::State::get()
{
    return _native->state() == webrtc::MediaStreamTrackInterface::kLive
        ? RtcTrackState::Live : RtcTrackState::Ended;
}

// ========================================================================
// RtcMediaStreamBridge
// ========================================================================

RtcMediaStreamBridge::RtcMediaStreamBridge(
    rtc::scoped_refptr<webrtc::MediaStreamInterface> native)
    : _native(native), _disposed(false)
{
}

RtcMediaStreamBridge::~RtcMediaStreamBridge()
{
    this->!RtcMediaStreamBridge();
}

RtcMediaStreamBridge::!RtcMediaStreamBridge()
{
    if (!_disposed)
    {
        _native = nullptr;
        _disposed = true;
    }
}

String^ RtcMediaStreamBridge::Id::get()
{
    return marshal_as<String^>(_native->id());
}

IReadOnlyList<IRtcVideoTrack^>^ RtcMediaStreamBridge::VideoTracks::get()
{
    auto list = gcnew List<IRtcVideoTrack^>();
    for (auto const& t : _native->GetVideoTracks())
        list->Add(gcnew RtcVideoTrackBridge(t));
    return list->AsReadOnly();
}

IReadOnlyList<IRtcAudioTrack^>^ RtcMediaStreamBridge::AudioTracks::get()
{
    auto list = gcnew List<IRtcAudioTrack^>();
    for (auto const& t : _native->GetAudioTracks())
        list->Add(gcnew RtcAudioTrackBridge(t));
    return list->AsReadOnly();
}

} // Native
} // WebRtc
} // Serenada
