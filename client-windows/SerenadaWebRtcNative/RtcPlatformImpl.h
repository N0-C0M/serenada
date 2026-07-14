#pragma once

// ============================================================================
// Platform implementation — initializes libwebrtc and creates factories.
// ============================================================================

namespace Serenada {
namespace WebRtc {
namespace Native {

using namespace System;
using namespace Serenada::Core::WebRtc;

/// <summary>
/// Windows implementation of IRtcPlatform.
/// Loads the libwebrtc native DLL and creates the peer connection factory.
/// </summary>
public ref class RtcPlatformImpl : public IRtcPlatform
{
public:
    RtcPlatformImpl();

    virtual void Initialize();
    virtual IRtcPeerConnectionFactory^ CreateFactory();
    virtual property bool IsSupported { bool get(); }

private:
    bool _initialized;
};

} // Native
} // WebRtc
} // Serenada
