#pragma once

// ============================================================================
// C++/CLI bridge: PeerConnection wrapper.
// Wraps webrtc::PeerConnectionInterface and exposes
// Serenada.Core.WebRtc.IRtcPeerConnection.
// ============================================================================

#include <memory>
#include <string>
#include <functional>

namespace webrtc
{
    class PeerConnectionInterface;
    class RtpSenderInterface;
    class RtpTransceiverInterface;
    class MediaStreamTrackInterface;
    class VideoTrackInterface;
    class AudioTrackInterface;
    class SessionDescriptionInterface;
    class IceCandidateInterface;
    struct PeerConnectionInterface::RTCConfiguration;

    template <typename T> class scoped_refptr;
}

namespace Serenada {
namespace WebRtc {
namespace Native {

using namespace System;
using namespace System::Threading::Tasks;
using namespace Serenada::Core::WebRtc;

// Helpers to convert between managed and native SDP/ICE types
ref class Marshalling
{
public:
    static webrtc::PeerConnectionInterface::RTCConfiguration
        ToNativeConfig(RtcConfiguration^ config);

    static RtcConfiguration^
        FromNativeConfig(webrtc::PeerConnectionInterface::RTCConfiguration const& config);

    static std::unique_ptr<webrtc::SessionDescriptionInterface>
        ToNativeSdp(RtcSessionDescription^ desc);

    static RtcSessionDescription^
        FromNativeSdp(webrtc::SessionDescriptionInterface const* desc);

    static std::unique_ptr<webrtc::IceCandidateInterface>
        ToNativeIce(RtcIceCandidate^ candidate);

    static RtcIceCandidate^
        FromNativeIce(webrtc::IceCandidateInterface const* candidate);
};

/// <summary>
/// Managed peer connection observer that forwards libwebrtc callbacks
/// to the managed IRtcPeerConnectionObserver interface.
/// </summary>
class PeerConnectionObserverAdapter : public webrtc::PeerConnectionObserver
{
public:
    explicit PeerConnectionObserverAdapter(
        gcroot<IRtcPeerConnectionObserver^> managedObserver);

    // webrtc::PeerConnectionObserver overrides
    void OnSignalingChange(
        webrtc::PeerConnectionInterface::SignalingState state) override;
    void OnIceConnectionChange(
        webrtc::PeerConnectionInterface::IceConnectionState state) override;
    void OnConnectionChange(
        webrtc::PeerConnectionInterface::PeerConnectionState state) override;
    void OnIceCandidate(const webrtc::IceCandidateInterface* candidate) override;
    void OnIceCandidatesRemoved(
        const std::vector<cricket::Candidate>& candidates) override;
    void OnAddTrack(
        rtc::scoped_refptr<webrtc::RtpReceiverInterface> receiver,
        const std::vector<rtc::scoped_refptr<webrtc::MediaStreamInterface>>& streams) override;
    void OnRemoveTrack(
        rtc::scoped_refptr<webrtc::RtpReceiverInterface> receiver) override;
    void OnRenegotiationNeeded() override;
    void OnIceGatheringChange(
        webrtc::PeerConnectionInterface::IceGatheringState state) override;
    void OnDataChannel(
        rtc::scoped_refptr<webrtc::DataChannelInterface> dataChannel) override;

private:
    gcroot<IRtcPeerConnectionObserver^> _observer;
};

/// <summary>
/// Managed wrapper around webrtc::PeerConnectionInterface.
/// </summary>
public ref class RtcPeerConnectionBridge : public IRtcPeerConnection
{
public:
    RtcPeerConnectionBridge(
        rtc::scoped_refptr<webrtc::PeerConnectionInterface> native);
    ~RtcPeerConnectionBridge();
    !RtcPeerConnectionBridge();

    virtual property RtcIceConnectionState IceConnectionState
        { RtcIceConnectionState get(); }
    virtual property RtcPeerConnectionState ConnectionState
        { RtcPeerConnectionState get(); }
    virtual property RtcSignalingState SignalingState
        { RtcSignalingState get(); }
    virtual property RtcIceGatheringState IceGatheringState
        { RtcIceGatheringState get(); }

    virtual IRtcRtpSender^ AddVideoTrack(IRtcVideoTrack^ track, IReadOnlyList<String^>^ streamIds);
    virtual IRtcRtpSender^ AddAudioTrack(IRtcAudioTrack^ track, IReadOnlyList<String^>^ streamIds);
    virtual IRtcRtpTransceiver^ AddTransceiver(RtcMediaType mediaType, RtcTransceiverDirection direction);
    virtual bool RemoveTrack(IRtcRtpSender^ sender);
    virtual property IReadOnlyList<IRtcRtpTransceiver^>^ Transceivers
        { IReadOnlyList<IRtcRtpTransceiver^>^ get(); }

    virtual Task<RtcSessionDescription^>^ CreateOfferAsync();
    virtual Task<RtcSessionDescription^>^ CreateAnswerAsync();
    virtual Task^ SetLocalDescriptionAsync(RtcSessionDescription^ desc);
    virtual Task^ SetRemoteDescriptionAsync(RtcSessionDescription^ desc);
    virtual Task^ AddIceCandidateAsync(RtcIceCandidate^ candidate);
    virtual Task^ RollbackLocalDescriptionAsync();

    virtual Task<IReadOnlyList<RtcStatsEntry^>^>^ GetStatsAsync();

    virtual void Close();
    virtual void SetConfiguration(RtcConfiguration^ config);
    virtual void RestartIce();

    property rtc::scoped_refptr<webrtc::PeerConnectionInterface> NativePeerConnection
    {
        rtc::scoped_refptr<webrtc::PeerConnectionInterface> get() { return _native; }
    }

private:
    rtc::scoped_refptr<webrtc::PeerConnectionInterface> _native;
    std::unique_ptr<PeerConnectionObserverAdapter> _observerAdapter;
    bool _disposed;

    // Convert between managed and native state enums
    static RtcIceConnectionState FromNativeIceState(
        webrtc::PeerConnectionInterface::IceConnectionState s);
    static RtcPeerConnectionState FromNativePeerState(
        webrtc::PeerConnectionInterface::PeerConnectionState s);
    static RtcSignalingState FromNativeSignalingState(
        webrtc::PeerConnectionInterface::SignalingState s);
    static RtcIceGatheringState FromNativeGatheringState(
        webrtc::PeerConnectionInterface::IceGatheringState s);
};

// Forward-declare track/transceiver/sender bridge types
ref class RtcVideoTrackBridge;
ref class RtcAudioTrackBridge;
ref class RtcRtpTransceiverBridge;
ref class RtcRtpSenderBridge;

/// <summary>Bridge for RTP sender.</summary>
public ref class RtcRtpSenderBridge : public IRtcRtpSender
{
public:
    RtcRtpSenderBridge(rtc::scoped_refptr<webrtc::RtpSenderInterface> native);
    virtual property String^ TrackId { String^ get(); }
    virtual property IRtcVideoTrack^ VideoTrack { IRtcVideoTrack^ get(); }
    virtual property IRtcAudioTrack^ AudioTrack { IRtcAudioTrack^ get(); }
    virtual void SetParameters(RtcRtpParameters^ parameters);
private:
    rtc::scoped_refptr<webrtc::RtpSenderInterface> _native;
};

/// <summary>Bridge for RTP transceiver.</summary>
public ref class RtcRtpTransceiverBridge : public IRtcRtpTransceiver
{
public:
    RtcRtpTransceiverBridge(
        rtc::scoped_refptr<webrtc::RtpTransceiverInterface> native);
    virtual property String^ Mid { String^ get(); }
    virtual property RtcMediaType MediaType { RtcMediaType get(); }
    virtual property IRtcRtpSender^ Sender { IRtcRtpSender^ get(); }
    virtual property IRtcVideoTrack^ ReceiverVideoTrack { IRtcVideoTrack^ get(); }
    virtual property IRtcAudioTrack^ ReceiverAudioTrack { IRtcAudioTrack^ get(); }
    virtual property RtcTransceiverDirection Direction
        { RtcTransceiverDirection get(); void set(RtcTransceiverDirection value); }
private:
    rtc::scoped_refptr<webrtc::RtpTransceiverInterface> _native;
    RtcRtpTransceiverDirection FromNativeDirection(
        webrtc::RtpTransceiverDirection d);
    webrtc::RtpTransceiverDirection ToNativeDirection(
        RtcTransceiverDirection d);
};

} // Native
} // WebRtc
} // Serenada
