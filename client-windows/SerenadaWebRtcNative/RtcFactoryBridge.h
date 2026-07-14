#pragma once

// ============================================================================
// C++/CLI bridge: PeerConnectionFactory wrapper.
// Wraps webrtc::PeerConnectionFactoryInterface and exposes it as
// Serenada.Core.WebRtc.IRtcPeerConnectionFactory.
// ============================================================================

#include <memory>

// Forward-declare libwebrtc types (no heavy includes in header)
namespace webrtc
{
    class PeerConnectionFactoryInterface;
    class PeerConnectionInterface;
    class VideoTrackSourceInterface;
    class AudioSourceInterface;
    class VideoTrackInterface;
    class AudioTrackInterface;
    class MediaStreamInterface;
    struct PeerConnectionInterface::RTCConfiguration;
}

namespace Serenada {
namespace WebRtc {
namespace Native {

using namespace System;
using namespace System::Collections::Generic;
using namespace Serenada::Core::WebRtc;

/// <summary>
/// Managed wrapper around webrtc::PeerConnectionFactoryInterface.
/// </summary>
public ref class RtcPeerConnectionFactoryBridge : public IRtcPeerConnectionFactory
{
public:
    RtcPeerConnectionFactoryBridge();
    ~RtcPeerConnectionFactoryBridge();
    !RtcPeerConnectionFactoryBridge();

    virtual IRtcPeerConnection^ CreatePeerConnection(
        RtcConfiguration^ config, IRtcPeerConnectionObserver^ observer);

    virtual IRtcVideoSource^ CreateVideoSource(bool isScreencast);
    virtual IRtcAudioSource^ CreateAudioSource();
    virtual IRtcVideoTrack^ CreateVideoTrack(String^ id, IRtcVideoSource^ source);
    virtual IRtcAudioTrack^ CreateAudioTrack(String^ id, IRtcAudioSource^ source);
    virtual IRtcMediaStream^ CreateLocalMediaStream(String^ id);

    // Expose native factory for bridge internals
    property webrtc::PeerConnectionFactoryInterface* NativeFactory
    {
        webrtc::PeerConnectionFactoryInterface* get() { return _native.get(); }
    }

private:
    std::shared_ptr<webrtc::PeerConnectionFactoryInterface> _native;
    bool _disposed;
};

/// <summary>
/// Managed wrapper around webrtc::VideoTrackSource (camera capture source).
/// </summary>
public ref class RtcVideoSourceBridge : public IRtcVideoSource
{
public:
    RtcVideoSourceBridge(webrtc::VideoTrackSourceInterface* native);
    ~RtcVideoSourceBridge();
    !RtcVideoSourceBridge();

    virtual property bool IsScreencast { bool get(); }
    virtual void OnFrameCaptured(IntPtr frameData, int width, int height, long timestampUs);

    property webrtc::VideoTrackSourceInterface* NativeSource
    {
        webrtc::VideoTrackSourceInterface* get() { return _native; }
    }

private:
    webrtc::VideoTrackSourceInterface* _native; // non-owning
    bool _disposed;
};

/// <summary>
/// Managed wrapper around webrtc::AudioSourceInterface.
/// </summary>
public ref class RtcAudioSourceBridge : public IRtcAudioSource
{
public:
    RtcAudioSourceBridge(webrtc::AudioSourceInterface* native);
    ~RtcAudioSourceBridge();
    !RtcAudioSourceBridge();

    property webrtc::AudioSourceInterface* NativeSource
    {
        webrtc::AudioSourceInterface* get() { return _native; }
    }

private:
    webrtc::AudioSourceInterface* _native; // non-owning
    bool _disposed;
};

} // Native
} // WebRtc
} // Serenada
