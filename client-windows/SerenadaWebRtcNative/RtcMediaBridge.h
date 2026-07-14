#pragma once

// ============================================================================
// C++/CLI bridge: Media track and stream wrappers.
// ============================================================================

#include <memory>

namespace webrtc
{
    class MediaStreamInterface;
    class MediaStreamTrackInterface;
    class VideoTrackInterface;
    class AudioTrackInterface;
    template <typename T> class scoped_refptr;
}

namespace Serenada {
namespace WebRtc {
namespace Native {

using namespace System;
using namespace System::Collections::Generic;
using namespace Serenada::Core::WebRtc;

/// <summary>Wrapper around webrtc::VideoTrackInterface.</summary>
public ref class RtcVideoTrackBridge : public IRtcVideoTrack
{
public:
    RtcVideoTrackBridge(rtc::scoped_refptr<webrtc::VideoTrackInterface> native);
    ~RtcVideoTrackBridge();
    !RtcVideoTrackBridge();

    virtual property String^ Id { String^ get(); }
    virtual property bool Enabled { bool get(); void set(bool value); }
    virtual property RtcTrackState State { RtcTrackState get(); }
    virtual void AddSink(IRtcVideoSink^ sink);
    virtual void RemoveSink(IRtcVideoSink^ sink);

    property webrtc::VideoTrackInterface* NativeTrack
        { webrtc::VideoTrackInterface* get() { return _native.get(); } }

private:
    rtc::scoped_refptr<webrtc::VideoTrackInterface> _native;
    bool _disposed;
};

/// <summary>Wrapper around webrtc::AudioTrackInterface.</summary>
public ref class RtcAudioTrackBridge : public IRtcAudioTrack
{
public:
    RtcAudioTrackBridge(rtc::scoped_refptr<webrtc::AudioTrackInterface> native);
    ~RtcAudioTrackBridge();
    !RtcAudioTrackBridge();

    virtual property String^ Id { String^ get(); }
    virtual property bool Enabled { bool get(); void set(bool value); }
    virtual property RtcTrackState State { RtcTrackState get(); }

    property webrtc::AudioTrackInterface* NativeTrack
        { webrtc::AudioTrackInterface* get() { return _native.get(); } }

private:
    rtc::scoped_refptr<webrtc::AudioTrackInterface> _native;
    bool _disposed;
};

/// <summary>Wrapper around webrtc::MediaStreamInterface.</summary>
public ref class RtcMediaStreamBridge : public IRtcMediaStream
{
public:
    RtcMediaStreamBridge(rtc::scoped_refptr<webrtc::MediaStreamInterface> native);
    ~RtcMediaStreamBridge();
    !RtcMediaStreamBridge();

    virtual property String^ Id { String^ get(); }
    virtual property IReadOnlyList<IRtcVideoTrack^>^ VideoTracks
        { IReadOnlyList<IRtcVideoTrack^>^ get(); }
    virtual property IReadOnlyList<IRtcAudioTrack^>^ AudioTracks
        { IReadOnlyList<IRtcAudioTrack^>^ get(); }

private:
    rtc::scoped_refptr<webrtc::MediaStreamInterface> _native;
    bool _disposed;
};

} // Native
} // WebRtc
} // Serenada
