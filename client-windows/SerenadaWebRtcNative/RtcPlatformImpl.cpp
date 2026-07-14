// ============================================================================
// RtcPlatformImpl — Windows platform initialization for libwebrtc.
// ============================================================================

#include "RtcPlatformImpl.h"
#include "RtcFactoryBridge.h"

// libwebrtc includes
#include "api/peer_connection_interface.h"
#include "rtc_base/ssl_adapter.h"
#include "rtc_base/network_monitor_factory.h"
#include "system_wrappers/include/field_trial.h"

namespace Serenada {
namespace WebRtc {
namespace Native {

RtcPlatformImpl::RtcPlatformImpl()
    : _initialized(false)
{
}

void RtcPlatformImpl::Initialize()
{
    if (_initialized) return;

    // Initialize SSL (required for DTLS in WebRTC)
    rtc::InitializeSSL();

    // Initialize Windows sockets
    // (libwebrtc on Windows handles WSAStartup internally)

    _initialized = true;
}

IRtcPeerConnectionFactory^ RtcPlatformImpl::CreateFactory()
{
    if (!_initialized)
        Initialize();

    return gcnew RtcPeerConnectionFactoryBridge();
}

bool RtcPlatformImpl::IsSupported::get()
{
    // On Windows, WebRTC is always supported if the native DLL loaded.
    // The constructor would fail with a DllNotFoundException otherwise.
    return true;
}

} // Native
} // WebRtc
} // Serenada
