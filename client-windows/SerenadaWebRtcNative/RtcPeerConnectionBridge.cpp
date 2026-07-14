// ============================================================================
// RtcPeerConnectionBridge — wraps webrtc::PeerConnectionInterface.
// ============================================================================

#include "RtcPeerConnectionBridge.h"
#include "RtcMediaBridge.h"

// libwebrtc includes
#include "api/peer_connection_interface.h"
#include "api/jsep.h"
#include "api/rtp_transceiver_interface.h"
#include "api/rtp_sender_interface.h"
#include "api/data_channel_interface.h"

using namespace msclr::interop;

namespace Serenada {
namespace WebRtc {
namespace Native {

// ========================================================================
// Marshalling helpers
// ========================================================================

webrtc::PeerConnectionInterface::RTCConfiguration
Marshalling::ToNativeConfig(RtcConfiguration^ config)
{
    webrtc::PeerConnectionInterface::RTCConfiguration native;

    native.continual_gathering_policy =
        config->ContinualGatheringPolicy
            ? webrtc::PeerConnectionInterface::GATHER_CONTINUALLY
            : webrtc::PeerConnectionInterface::GATHER_ONCE;

    for each (auto server in config->IceServers)
    {
        webrtc::PeerConnectionInterface::IceServer ice;
        for each (auto url in server->Urls)
            ice.urls.push_back(marshal_as<std::string>(url));
        if (server->Username)
            ice.username = marshal_as<std::string>(server->Username);
        if (server->Password)
            ice.password = marshal_as<std::string>(server->Password);
        native.servers.push_back(ice);
    }

    // Enable Unified Plan SDP semantics (required for independent content video)
    native.sdp_semantics = webrtc::SdpSemantics::kUnifiedPlan;

    return native;
}

RtcConfiguration^ Marshalling::FromNativeConfig(
    webrtc::PeerConnectionInterface::RTCConfiguration const& config)
{
    auto managed = gcnew RtcConfiguration();
    auto servers = gcnew List<RtcIceServer^>();
    for (auto const& s : config.servers)
    {
        auto urls = gcnew List<String^>();
        for (auto const& u : s.urls)
            urls->Add(marshal_as<String^>(u));
        servers->Add(gcnew RtcIceServer
        {
            Urls = urls->AsReadOnly(),
            Username = s.username.empty() ? nullptr : marshal_as<String^>(s.username),
            Password = s.password.empty() ? nullptr : marshal_as<String^>(s.password),
        });
    }
    return managed with { IceServers = servers->AsReadOnly() };
}

std::unique_ptr<webrtc::SessionDescriptionInterface>
Marshalling::ToNativeSdp(RtcSessionDescription^ desc)
{
    auto type = desc->Type == RtcSdpType::Offer
        ? webrtc::SdpType::kOffer
        : webrtc::SdpType::kAnswer;
    return webrtc::CreateSessionDescription(
        type, marshal_as<std::string>(desc->Sdp));
}

RtcSessionDescription^ Marshalling::FromNativeSdp(
    webrtc::SessionDescriptionInterface const* desc)
{
    if (!desc) return nullptr;
    std::string sdp;
    desc->ToString(&sdp);
    auto type = desc->GetType() == webrtc::SdpType::kOffer
        ? RtcSdpType::Offer : RtcSdpType::Answer;
    return gcnew RtcSessionDescription
    {
        Type = type,
        Sdp = marshal_as<String^>(sdp),
    };
}

std::unique_ptr<webrtc::IceCandidateInterface>
Marshalling::ToNativeIce(RtcIceCandidate^ candidate)
{
    webrtc::SdpParseError error;
    return webrtc::CreateIceCandidate(
        marshal_as<std::string>(candidate->SdpMid),
        candidate->SdpMLineIndex,
        marshal_as<std::string>(candidate->Candidate),
        &error);
}

RtcIceCandidate^ Marshalling::FromNativeIce(
    webrtc::IceCandidateInterface const* candidate)
{
    if (!candidate) return nullptr;
    std::string sdp;
    candidate->ToString(&sdp);
    return gcnew RtcIceCandidate
    {
        SdpMid = marshal_as<String^>(candidate->sdp_mid()),
        SdpMLineIndex = candidate->sdp_mline_index(),
        Candidate = marshal_as<String^>(sdp),
    };
}

// ========================================================================
// PeerConnectionObserverAdapter
// ========================================================================

PeerConnectionObserverAdapter::PeerConnectionObserverAdapter(
    gcroot<IRtcPeerConnectionObserver^> managedObserver)
    : _observer(managedObserver)
{
}

void PeerConnectionObserverAdapter::OnSignalingChange(
    webrtc::PeerConnectionInterface::SignalingState state)
{
    _observer->OnSignalingChange(
        RtcPeerConnectionBridge::FromNativeSignalingState(state));
}

void PeerConnectionObserverAdapter::OnIceConnectionChange(
    webrtc::PeerConnectionInterface::IceConnectionState state)
{
    _observer->OnIceConnectionChange(
        RtcPeerConnectionBridge::FromNativeIceState(state));
}

void PeerConnectionObserverAdapter::OnConnectionChange(
    webrtc::PeerConnectionInterface::PeerConnectionState state)
{
    _observer->OnConnectionChange(
        RtcPeerConnectionBridge::FromNativePeerState(state));
}

void PeerConnectionObserverAdapter::OnIceCandidate(
    const webrtc::IceCandidateInterface* candidate)
{
    _observer->OnIceCandidate(Marshalling::FromNativeIce(candidate));
}

void PeerConnectionObserverAdapter::OnIceCandidatesRemoved(
    const std::vector<cricket::Candidate>& candidates)
{
    auto managed = gcnew array<RtcIceCandidate^>((int)candidates.size());
    // Not currently used by Serenada — forwarded for completeness
    _observer->OnIceCandidatesRemoved(managed);
}

void PeerConnectionObserverAdapter::OnAddTrack(
    rtc::scoped_refptr<webrtc::RtpReceiverInterface> receiver,
    const std::vector<rtc::scoped_refptr<webrtc::MediaStreamInterface>>& streams)
{
    auto* mediaTrack = receiver->track().get();
    if (!mediaTrack) return;

    auto streamId = streams.empty() ? "default" : streams[0]->id();

    if (mediaTrack->kind() == webrtc::MediaStreamTrackInterface::kVideoKind)
    {
        auto* videoTrack = static_cast<webrtc::VideoTrackInterface*>(mediaTrack);
        auto managedTrack = gcnew RtcVideoTrackBridge(
            rtc::scoped_refptr<webrtc::VideoTrackInterface>(videoTrack));
        _observer->OnAddTrack(managedTrack, nullptr, marshal_as<String^>(streamId));
    }
    else if (mediaTrack->kind() == webrtc::MediaStreamTrackInterface::kAudioKind)
    {
        auto* audioTrack = static_cast<webrtc::AudioTrackInterface*>(mediaTrack);
        auto managedTrack = gcnew RtcAudioTrackBridge(
            rtc::scoped_refptr<webrtc::AudioTrackInterface>(audioTrack));
        _observer->OnAddTrack(nullptr, managedTrack, marshal_as<String^>(streamId));
    }
}

void PeerConnectionObserverAdapter::OnRemoveTrack(
    rtc::scoped_refptr<webrtc::RtpReceiverInterface> receiver)
{
    auto* mediaTrack = receiver->track().get();
    if (!mediaTrack) return;

    if (mediaTrack->kind() == webrtc::MediaStreamTrackInterface::kVideoKind)
    {
        auto* vt = static_cast<webrtc::VideoTrackInterface*>(mediaTrack);
        auto managed = gcnew RtcVideoTrackBridge(
            rtc::scoped_refptr<webrtc::VideoTrackInterface>(vt));
        _observer->OnRemoveTrack(managed, nullptr);
    }
    else
    {
        auto* at = static_cast<webrtc::AudioTrackInterface*>(mediaTrack);
        auto managed = gcnew RtcAudioTrackBridge(
            rtc::scoped_refptr<webrtc::AudioTrackInterface>(at));
        _observer->OnRemoveTrack(nullptr, managed);
    }
}

void PeerConnectionObserverAdapter::OnRenegotiationNeeded()
{
    _observer->OnRenegotiationNeeded();
}

void PeerConnectionObserverAdapter::OnIceGatheringChange(
    webrtc::PeerConnectionInterface::IceGatheringState state)
{
    _observer->OnIceGatheringChange(
        RtcPeerConnectionBridge::FromNativeGatheringState(state));
}

void PeerConnectionObserverAdapter::OnDataChannel(
    rtc::scoped_refptr<webrtc::DataChannelInterface> dataChannel)
{
    // Data channels not used by Serenada
}

// ========================================================================
// RtcPeerConnectionBridge
// ========================================================================

RtcPeerConnectionBridge::RtcPeerConnectionBridge(
    rtc::scoped_refptr<webrtc::PeerConnectionInterface> native)
    : _native(native), _disposed(false)
{
    // Observer adapter is set during factory creation via PeerConnectionDependencies
}

RtcPeerConnectionBridge::~RtcPeerConnectionBridge()
{
    this->!RtcPeerConnectionBridge();
}

RtcPeerConnectionBridge::!RtcPeerConnectionBridge()
{
    if (!_disposed)
    {
        Close();
        _disposed = true;
    }
}

// ── State properties ────────────────────────────────────────

RtcIceConnectionState RtcPeerConnectionBridge::IceConnectionState::get()
{
    return FromNativeIceState(_native->ice_connection_state());
}

RtcPeerConnectionState RtcPeerConnectionBridge::ConnectionState::get()
{
    return FromNativePeerState(_native->peer_connection_state());
}

RtcSignalingState RtcPeerConnectionBridge::SignalingState::get()
{
    return FromNativeSignalingState(_native->signaling_state());
}

RtcIceGatheringState RtcPeerConnectionBridge::IceGatheringState::get()
{
    return FromNativeGatheringState(_native->ice_gathering_state());
}

// ── Tracks ──────────────────────────────────────────────────

IRtcRtpSender^ RtcPeerConnectionBridge::AddVideoTrack(
    IRtcVideoTrack^ track, IReadOnlyList<String^>^ streamIds)
{
    auto nativeTrack = safe_cast<RtcVideoTrackBridge^>(track)->NativeTrack;

    std::vector<std::string> ids;
    for each (auto sid in streamIds)
        ids.push_back(marshal_as<std::string>(sid));

    auto result = _native->AddTrack(nativeTrack, ids);
    if (!result.ok())
    {
        throw gcnew InvalidOperationException(
            "AddVideoTrack failed: " + gcnew String(result.error().message()));
    }

    return gcnew RtcRtpSenderBridge(result.MoveValue());
}

IRtcRtpSender^ RtcPeerConnectionBridge::AddAudioTrack(
    IRtcAudioTrack^ track, IReadOnlyList<String^>^ streamIds)
{
    auto nativeTrack = safe_cast<RtcAudioTrackBridge^>(track)->NativeTrack;

    std::vector<std::string> ids;
    for each (auto sid in streamIds)
        ids.push_back(marshal_as<std::string>(sid));

    auto result = _native->AddTrack(nativeTrack, ids);
    if (!result.ok())
    {
        throw gcnew InvalidOperationException(
            "AddAudioTrack failed: " + gcnew String(result.error().message()));
    }

    return gcnew RtcRtpSenderBridge(result.MoveValue());
}

IRtcRtpTransceiver^ RtcPeerConnectionBridge::AddTransceiver(
    RtcMediaType mediaType, RtcTransceiverDirection direction)
{
    cricket::MediaType nativeMediaType = mediaType == RtcMediaType::Audio
        ? cricket::MEDIA_TYPE_AUDIO : cricket::MEDIA_TYPE_VIDEO;

    auto nativeDirection = [](RtcTransceiverDirection d)
    {
        switch (d)
        {
        case RtcTransceiverDirection::SendRecv: return webrtc::RtpTransceiverDirection::kSendRecv;
        case RtcTransceiverDirection::SendOnly: return webrtc::RtpTransceiverDirection::kSendOnly;
        case RtcTransceiverDirection::RecvOnly: return webrtc::RtpTransceiverDirection::kRecvOnly;
        default: return webrtc::RtpTransceiverDirection::kInactive;
        }
    }(direction);

    auto result = _native->AddTransceiver(nativeMediaType,
        webrtc::RtpTransceiverInit(nativeDirection));
    if (!result.ok())
    {
        throw gcnew InvalidOperationException(
            "AddTransceiver failed: " + gcnew String(result.error().message()));
    }

    return gcnew RtcRtpTransceiverBridge(result.MoveValue());
}

bool RtcPeerConnectionBridge::RemoveTrack(IRtcRtpSender^ sender)
{
    auto nativeSender = safe_cast<RtcRtpSenderBridge^>(sender);
    auto error = _native->RemoveTrackOrError(nativeSender->_native);
    return error.ok();
}

IReadOnlyList<IRtcRtpTransceiver^>^ RtcPeerConnectionBridge::Transceivers::get()
{
    auto list = gcnew List<IRtcRtpTransceiver^>();
    for (auto const& t : _native->GetTransceivers())
        list->Add(gcnew RtcRtpTransceiverBridge(t));
    return list->AsReadOnly();
}

// ── Negotiation ─────────────────────────────────────────────

Task<RtcSessionDescription^>^ RtcPeerConnectionBridge::CreateOfferAsync()
{
    return Task::Run(gcnew Func<RtcSessionDescription^>(this,
        &RtcPeerConnectionBridge::CreateOfferSync));
}

RtcSessionDescription^ RtcPeerConnectionBridge::CreateOfferSync()
{
    webrtc::PeerConnectionInterface::RTCOfferAnswerOptions options;
    options.offer_to_receive_audio = true;
    options.offer_to_receive_video = true;

    auto result = _native->CreateOfferOrError(options);
    if (!result.ok())
        throw gcnew InvalidOperationException(
            "CreateOffer failed: " + gcnew String(result.error().message()));

    return Marshalling::FromNativeSdp(result.MoveValue().release());
}

Task<RtcSessionDescription^>^ RtcPeerConnectionBridge::CreateAnswerAsync()
{
    return Task::Run(gcnew Func<RtcSessionDescription^>(this,
        &RtcPeerConnectionBridge::CreateAnswerSync));
}

RtcSessionDescription^ RtcPeerConnectionBridge::CreateAnswerSync()
{
    webrtc::PeerConnectionInterface::RTCOfferAnswerOptions options;

    auto result = _native->CreateAnswerOrError(options);
    if (!result.ok())
        throw gcnew InvalidOperationException(
            "CreateAnswer failed: " + gcnew String(result.error().message()));

    return Marshalling::FromNativeSdp(result.MoveValue().release());
}

Task^ RtcPeerConnectionBridge::SetLocalDescriptionAsync(RtcSessionDescription^ desc)
{
    return Task::Run(gcnew Action(this, &RtcPeerConnectionBridge::SetLocalDescSync), desc);
}

void RtcPeerConnectionBridge::SetLocalDescSync(RtcSessionDescription^ desc)
{
    auto nativeSdp = Marshalling::ToNativeSdp(desc);
    auto error = _native->SetLocalDescription(std::move(nativeSdp));
    if (!error.ok())
        throw gcnew InvalidOperationException(
            "SetLocalDescription failed: " + gcnew String(error.message()));
}

Task^ RtcPeerConnectionBridge::SetRemoteDescriptionAsync(RtcSessionDescription^ desc)
{
    return Task::Run(gcnew Action(this, &RtcPeerConnectionBridge::SetRemoteDescSync), desc);
}

void RtcPeerConnectionBridge::SetRemoteDescSync(RtcSessionDescription^ desc)
{
    auto nativeSdp = Marshalling::ToNativeSdp(desc);
    auto error = _native->SetRemoteDescription(std::move(nativeSdp));
    if (!error.ok())
        throw gcnew InvalidOperationException(
            "SetRemoteDescription failed: " + gcnew String(error.message()));
}

Task^ RtcPeerConnectionBridge::AddIceCandidateAsync(RtcIceCandidate^ candidate)
{
    return Task::Run(gcnew Action(this, &RtcPeerConnectionBridge::AddIceSync), candidate);
}

void RtcPeerConnectionBridge::AddIceSync(RtcIceCandidate^ candidate)
{
    auto nativeIce = Marshalling::ToNativeIce(candidate);
    auto error = _native->AddIceCandidate(std::move(nativeIce));
    if (!error.ok())
        throw gcnew InvalidOperationException(
            "AddIceCandidate failed: " + gcnew String(error.message()));
}

Task^ RtcPeerConnectionBridge::RollbackLocalDescriptionAsync()
{
    return Task::Run(gcnew Action([this]()
    {
        _native->Rollback();
    }));
}

// ── Stats ───────────────────────────────────────────────────

Task<IReadOnlyList<RtcStatsEntry^>^>^ RtcPeerConnectionBridge::GetStatsAsync()
{
    return Task::Run(gcnew Func<IReadOnlyList<RtcStatsEntry^>^>([this]()
    {
        // Stats collection requires a callback-based API.
        // For now, return an empty list — full implementation in Phase 3
        // using webrtc::RTCStatsCollectorCallback.
        auto list = gcnew List<RtcStatsEntry^>();
        return (IReadOnlyList<RtcStatsEntry^>^)list->AsReadOnly();
    }));
}

// ── Lifecycle ───────────────────────────────────────────────

void RtcPeerConnectionBridge::Close()
{
    if (_native)
    {
        _native->Close();
        _native = nullptr;
    }
}

void RtcPeerConnectionBridge::SetConfiguration(RtcConfiguration^ config)
{
    auto nativeConfig = Marshalling::ToNativeConfig(config);
    auto error = _native->SetConfiguration(nativeConfig);
    if (!error.ok())
        throw gcnew InvalidOperationException(
            "SetConfiguration failed: " + gcnew String(error.message()));
}

void RtcPeerConnectionBridge::RestartIce()
{
    _native->RestartIce();
}

// ── Enum conversions ───────────────────────────────────────

RtcIceConnectionState RtcPeerConnectionBridge::FromNativeIceState(
    webrtc::PeerConnectionInterface::IceConnectionState s)
{
    switch (s)
    {
    case webrtc::PeerConnectionInterface::kIceConnectionNew:          return RtcIceConnectionState::New;
    case webrtc::PeerConnectionInterface::kIceConnectionChecking:     return RtcIceConnectionState::Checking;
    case webrtc::PeerConnectionInterface::kIceConnectionConnected:    return RtcIceConnectionState::Connected;
    case webrtc::PeerConnectionInterface::kIceConnectionCompleted:    return RtcIceConnectionState::Completed;
    case webrtc::PeerConnectionInterface::kIceConnectionFailed:       return RtcIceConnectionState::Failed;
    case webrtc::PeerConnectionInterface::kIceConnectionDisconnected: return RtcIceConnectionState::Disconnected;
    case webrtc::PeerConnectionInterface::kIceConnectionClosed:       return RtcIceConnectionState::Closed;
    default: return RtcIceConnectionState::Closed;
    }
}

RtcPeerConnectionState RtcPeerConnectionBridge::FromNativePeerState(
    webrtc::PeerConnectionInterface::PeerConnectionState s)
{
    switch (s)
    {
    case webrtc::PeerConnectionInterface::PeerConnectionState::kNew:          return RtcPeerConnectionState::New;
    case webrtc::PeerConnectionInterface::PeerConnectionState::kConnecting:   return RtcPeerConnectionState::Connecting;
    case webrtc::PeerConnectionInterface::PeerConnectionState::kConnected:    return RtcPeerConnectionState::Connected;
    case webrtc::PeerConnectionInterface::PeerConnectionState::kDisconnected: return RtcPeerConnectionState::Disconnected;
    case webrtc::PeerConnectionInterface::PeerConnectionState::kFailed:       return RtcPeerConnectionState::Failed;
    case webrtc::PeerConnectionInterface::PeerConnectionState::kClosed:       return RtcPeerConnectionState::Closed;
    default: return RtcPeerConnectionState::Closed;
    }
}

RtcSignalingState RtcPeerConnectionBridge::FromNativeSignalingState(
    webrtc::PeerConnectionInterface::SignalingState s)
{
    switch (s)
    {
    case webrtc::PeerConnectionInterface::kStable:            return RtcSignalingState::Stable;
    case webrtc::PeerConnectionInterface::kHaveLocalOffer:    return RtcSignalingState::HaveLocalOffer;
    case webrtc::PeerConnectionInterface::kHaveLocalPrAnswer: return RtcSignalingState::HaveLocalPrAnswer;
    case webrtc::PeerConnectionInterface::kHaveRemoteOffer:   return RtcSignalingState::HaveRemoteOffer;
    case webrtc::PeerConnectionInterface::kHaveRemotePrAnswer:return RtcSignalingState::HaveRemotePrAnswer;
    case webrtc::PeerConnectionInterface::kClosed:            return RtcSignalingState::Closed;
    default: return RtcSignalingState::Closed;
    }
}

RtcIceGatheringState RtcPeerConnectionBridge::FromNativeGatheringState(
    webrtc::PeerConnectionInterface::IceGatheringState s)
{
    switch (s)
    {
    case webrtc::PeerConnectionInterface::kIceGatheringNew:       return RtcIceGatheringState::New;
    case webrtc::PeerConnectionInterface::kIceGatheringGathering: return RtcIceGatheringState::Gathering;
    case webrtc::PeerConnectionInterface::kIceGatheringComplete:  return RtcIceGatheringState::Complete;
    default: return RtcIceGatheringState::New;
    }
}

// ========================================================================
// RtcRtpSenderBridge
// ========================================================================

RtcRtpSenderBridge::RtcRtpSenderBridge(
    rtc::scoped_refptr<webrtc::RtpSenderInterface> native)
    : _native(native)
{
}

String^ RtcRtpSenderBridge::TrackId::get()
{
    auto* track = _native->track().get();
    return track ? marshal_as<String^>(track->id()) : nullptr;
}

IRtcVideoTrack^ RtcRtpSenderBridge::VideoTrack::get()
{
    auto* track = _native->track().get();
    if (track && track->kind() == webrtc::MediaStreamTrackInterface::kVideoKind)
    {
        auto* vt = static_cast<webrtc::VideoTrackInterface*>(track);
        return gcnew RtcVideoTrackBridge(
            rtc::scoped_refptr<webrtc::VideoTrackInterface>(vt));
    }
    return nullptr;
}

void RtcRtpSenderBridge::SetParameters(RtcRtpParameters^ parameters)
{
    auto nativeParams = _native->GetParameters();
    if (parameters->MaxBitrateBps.HasValue)
        nativeParams.encodings[0].max_bitrate_bps = parameters->MaxBitrateBps.Value;
    if (parameters->MaxFramerate.HasValue)
        nativeParams.encodings[0].max_framerate = parameters->MaxFramerate.Value;
    _native->SetParameters(nativeParams);
}

// ========================================================================
// RtcRtpTransceiverBridge
// ========================================================================

RtcRtpTransceiverBridge::RtcRtpTransceiverBridge(
    rtc::scoped_refptr<webrtc::RtpTransceiverInterface> native)
    : _native(native)
{
}

String^ RtcRtpTransceiverBridge::Mid::get()
{
    return _native->mid() ? marshal_as<String^>(*_native->mid()) : nullptr;
}

RtcMediaType RtcRtpTransceiverBridge::MediaType::get()
{
    return _native->media_type() == cricket::MEDIA_TYPE_AUDIO
        ? RtcMediaType::Audio : RtcMediaType::Video;
}

IRtcRtpSender^ RtcRtpTransceiverBridge::Sender::get()
{
    return gcnew RtcRtpSenderBridge(_native->sender());
}

IRtcVideoTrack^ RtcRtpTransceiverBridge::ReceiverVideoTrack::get()
{
    auto* track = _native->receiver()->track().get();
    if (track && track->kind() == webrtc::MediaStreamTrackInterface::kVideoKind)
    {
        auto* vt = static_cast<webrtc::VideoTrackInterface*>(track);
        return gcnew RtcVideoTrackBridge(
            rtc::scoped_refptr<webrtc::VideoTrackInterface>(vt));
    }
    return nullptr;
}

RtcTransceiverDirection RtcRtpTransceiverBridge::Direction::get()
{
    return FromNativeDirection(_native->direction());
}

void RtcRtpTransceiverBridge::Direction::set(RtcTransceiverDirection value)
{
    _native->SetDirectionWithError(ToNativeDirection(value));
}

RtcTransceiverDirection RtcRtpTransceiverBridge::FromNativeDirection(
    webrtc::RtpTransceiverDirection d)
{
    switch (d)
    {
    case webrtc::RtpTransceiverDirection::kSendRecv: return RtcTransceiverDirection::SendRecv;
    case webrtc::RtpTransceiverDirection::kSendOnly: return RtcTransceiverDirection::SendOnly;
    case webrtc::RtpTransceiverDirection::kRecvOnly: return RtcTransceiverDirection::RecvOnly;
    default: return RtcTransceiverDirection::Inactive;
    }
}

webrtc::RtpTransceiverDirection RtcRtpTransceiverBridge::ToNativeDirection(
    RtcTransceiverDirection d)
{
    switch (d)
    {
    case RtcTransceiverDirection::SendRecv: return webrtc::RtpTransceiverDirection::kSendRecv;
    case RtcTransceiverDirection::SendOnly: return webrtc::RtpTransceiverDirection::kSendOnly;
    case RtcTransceiverDirection::RecvOnly: return webrtc::RtpTransceiverDirection::kRecvOnly;
    default: return webrtc::RtpTransceiverDirection::kInactive;
    }
}

} // Native
} // WebRtc
} // Serenada
