// ============================================================================
// RtcFactoryBridge — wraps webrtc::PeerConnectionFactoryInterface.
// ============================================================================

#include "RtcFactoryBridge.h"
#include "RtcPeerConnectionBridge.h"
#include "RtcMediaBridge.h"

// libwebrtc includes
#include "api/peer_connection_interface.h"
#include "api/create_peerconnection_factory.h"
#include "api/video_codecs/builtin_video_decoder_factory.h"
#include "api/video_codecs/builtin_video_encoder_factory.h"
#include "api/audio_codecs/builtin_audio_decoder_factory.h"
#include "api/audio_codecs/builtin_audio_encoder_factory.h"
#include "media/engine/webrtc_media_engine.h"
#include "rtc_base/thread.h"

namespace Serenada {
namespace WebRtc {
namespace Native {

// ========================================================================
// RtcPeerConnectionFactoryBridge
// ========================================================================

RtcPeerConnectionFactoryBridge::RtcPeerConnectionFactoryBridge()
    : _disposed(false)
{
    // Create networking threads (libwebrtc requires these)
    auto networkThread = rtc::Thread::CreateWithSocketServer();
    networkThread->SetName("webrtc_network", nullptr);
    networkThread->Start();

    auto workerThread = rtc::Thread::Create();
    workerThread->SetName("webrtc_worker", nullptr);
    workerThread->Start();

    auto signalingThread = rtc::Thread::Create();
    signalingThread->SetName("webrtc_signaling", nullptr);
    signalingThread->Start();

    // Create the factory
    _native = webrtc::CreatePeerConnectionFactory(
        networkThread.get(),     // network_thread
        workerThread.get(),      // worker_thread
        signalingThread.get(),   // signaling_thread
        nullptr,                 // default_adm
        webrtc::CreateBuiltinAudioEncoderFactory(),
        webrtc::CreateBuiltinAudioDecoderFactory(),
        webrtc::CreateBuiltinVideoEncoderFactory(),
        webrtc::CreateBuiltinVideoDecoderFactory(),
        nullptr,                 // audio_mixer
        nullptr                  // audio_processing
    );

    if (!_native)
    {
        throw gcnew InvalidOperationException(
            "Failed to create PeerConnectionFactory.");
    }
}

RtcPeerConnectionFactoryBridge::~RtcPeerConnectionFactoryBridge()
{
    this->!RtcPeerConnectionFactoryBridge();
}

RtcPeerConnectionFactoryBridge::!RtcPeerConnectionFactoryBridge()
{
    if (!_disposed)
    {
        _native = nullptr;
        _disposed = true;
    }
}

IRtcPeerConnection^ RtcPeerConnectionFactoryBridge::CreatePeerConnection(
    RtcConfiguration^ config, IRtcPeerConnectionObserver^ observer)
{
    auto nativeConfig = Marshalling::ToNativeConfig(config);

    // Create the observer adapter that bridges back to managed
    auto observerAdapter =
        std::make_unique<PeerConnectionObserverAdapter>(observer);

    webrtc::PeerConnectionDependencies deps(observerAdapter.get());
    auto pc = _native->CreatePeerConnectionOrError(
        nativeConfig, std::move(deps));

    if (!pc.ok())
    {
        throw gcnew InvalidOperationException(
            "Failed to create PeerConnection: " +
            gcnew String(pc.error().message()));
    }

    auto bridge = gcnew RtcPeerConnectionBridge(pc.MoveValue());

    // Transfer observer adapter ownership to the bridge
    // (the bridge keeps it alive while the PC lives)

    return bridge;
}

IRtcVideoSource^ RtcPeerConnectionFactoryBridge::CreateVideoSource(bool isScreencast)
{
    auto nativeSource = _native->CreateVideoSourceOrError(isScreencast);
    if (!nativeSource.ok())
    {
        throw gcnew InvalidOperationException(
            "Failed to create video source: " +
            gcnew String(nativeSource.error().message()));
    }
    return gcnew RtcVideoSourceBridge(nativeSource.MoveValue().release());
}

IRtcAudioSource^ RtcPeerConnectionFactoryBridge::CreateAudioSource()
{
    cricket::AudioOptions options;
    options.echo_cancellation = true;
    options.noise_suppression = true;
    options.auto_gain_control = true;

    auto nativeSource = _native->CreateAudioSourceOrError(options);
    if (!nativeSource.ok())
    {
        throw gcnew InvalidOperationException(
            "Failed to create audio source: " +
            gcnew String(nativeSource.error().message()));
    }
    return gcnew RtcAudioSourceBridge(nativeSource.MoveValue().release());
}

IRtcVideoTrack^ RtcPeerConnectionFactoryBridge::CreateVideoTrack(
    String^ id, IRtcVideoSource^ source)
{
    auto nativeSource = safe_cast<RtcVideoSourceBridge^>(source)->NativeSource;
    auto nativeTrack = _native->CreateVideoTrack(
        msclr::interop::marshal_as<std::string>(id),
        nativeSource);
    return gcnew RtcVideoTrackBridge(nativeTrack);
}

IRtcAudioTrack^ RtcPeerConnectionFactoryBridge::CreateAudioTrack(
    String^ id, IRtcAudioSource^ source)
{
    auto nativeSource = safe_cast<RtcAudioSourceBridge^>(source)->NativeSource;
    auto nativeTrack = _native->CreateAudioTrack(
        msclr::interop::marshal_as<std::string>(id),
        nativeSource);
    return gcnew RtcAudioTrackBridge(nativeTrack);
}

IRtcMediaStream^ RtcPeerConnectionFactoryBridge::CreateLocalMediaStream(String^ id)
{
    auto nativeStream = _native->CreateLocalMediaStream(
        msclr::interop::marshal_as<std::string>(id));
    return gcnew RtcMediaStreamBridge(nativeStream);
}

// ========================================================================
// RtcVideoSourceBridge
// ========================================================================

RtcVideoSourceBridge::RtcVideoSourceBridge(webrtc::VideoTrackSourceInterface* native)
    : _native(native), _disposed(false)
{
}

RtcVideoSourceBridge::~RtcVideoSourceBridge()
{
    this->!RtcVideoSourceBridge();
}

RtcVideoSourceBridge::!RtcVideoSourceBridge()
{
    if (!_disposed)
    {
        // _native is owned by the track; do not delete
        _native = nullptr;
        _disposed = true;
    }
}

bool RtcVideoSourceBridge::IsScreencast::get()
{
    return _native->is_screencast();
}

void RtcVideoSourceBridge::OnFrameCaptured(
    IntPtr frameData, int width, int height, long timestampUs)
{
    // Convert raw frame data to a webrtc::VideoFrame and push it into the source.
    // This is called by LocalMediaManager when it captures a camera frame.
    //
    // The actual frame push depends on the frame type:
    // - Camera frames: use webrtc::VideoFrame::Builder with a native buffer
    // - For now, this is a stub — the capture pipeline is wired in Phase 3.
    //
    // TODO: Implement when LocalMediaManager camera capture is integrated.
}

// ========================================================================
// RtcAudioSourceBridge
// ========================================================================

RtcAudioSourceBridge::RtcAudioSourceBridge(webrtc::AudioSourceInterface* native)
    : _native(native), _disposed(false)
{
}

RtcAudioSourceBridge::~RtcAudioSourceBridge()
{
    this->!RtcAudioSourceBridge();
}

RtcAudioSourceBridge::!RtcAudioSourceBridge()
{
    if (!_disposed)
    {
        _native = nullptr;
        _disposed = true;
    }
}

} // Native
} // WebRtc
} // Serenada
