# SerenadaWebRtcNative — C++/CLI Bridge for libwebrtc

Mixed-mode C++/CLI DLL that wraps **Google's libwebrtc** native C++ library
and exposes it as managed .NET interfaces (`Serenada.Core.WebRtc.*`).

## Architecture

```
┌──────────────────────────────────────────────┐
│ SerenadaCore (C# SDK)                        │
│   ├── WebRtcEngine.cs                        │
│   ├── PeerConnectionSlot.cs                  │
│   └── WebRtc/*.cs  (managed interfaces)      │
└──────────────────┬───────────────────────────┘
                   │ references
┌──────────────────▼───────────────────────────┐
│ SerenadaWebRtcNative (C++/CLI mixed DLL)     │
│   ├── RtcPeerConnectionBridge                │
│   ├── RtcFactoryBridge                       │
│   ├── RtcMediaBridge                         │
│   └── RtcVideoSinkAdapter                    │
└──────────────────┬───────────────────────────┘
                   │ links
┌──────────────────▼───────────────────────────┐
│ libwebrtc (native C++)                       │
│   webrtc.lib → PeerConnection,               │
│   PeerConnectionFactory, VideoTrack, etc.    │
└──────────────────────────────────────────────┘
```

## Prerequisites

1. **Visual Studio 2022** with "Desktop development with C++" workload
   - Must include C++/CLI support (v143 build tools)
2. **libwebrtc** built for Windows x64
   - See `../scripts/build-libwebrtc.ps1`
   - Or use a prebuilt binary package

## Building libwebrtc

```powershell
# One-time setup: build libwebrtc for Windows x64
cd client-windows/scripts
.\build-libwebrtc.ps1 -OutputDir $env:USERPROFILE\webrtc-build

# After the build completes, set the environment variable:
$env:WEBRTC_ROOT = "$env:USERPROFILE\webrtc-build\src"
```

## Building the Bridge

```powershell
# From Visual Studio Developer PowerShell:
cd client-windows

# Build the full solution (bridge + SDK)
msbuild SerenadaWindows.sln /p:Configuration=Release /p:Platform=x64

# Or build just the bridge:
msbuild SerenadaWebRtcNative\SerenadaWebRtcNative.vcxproj  `
    /p:Configuration=Release /p:Platform=x64               `
    /p:WebRtcRoot=%WEBRTC_ROOT%
```

## Managed Interface → Native Mapping

| C# Interface (`Serenada.Core.WebRtc`) | C++/CLI Bridge Class | Native libwebrtc Type |
|---|---|---|
| `IRtcPeerConnectionFactory` | `RtcPeerConnectionFactoryBridge` | `webrtc::PeerConnectionFactoryInterface` |
| `IRtcPeerConnection` | `RtcPeerConnectionBridge` | `webrtc::PeerConnectionInterface` |
| `IRtcPeerConnectionObserver` | `PeerConnectionObserverAdapter` | `webrtc::PeerConnectionObserver` |
| `IRtcVideoTrack` | `RtcVideoTrackBridge` | `webrtc::VideoTrackInterface` |
| `IRtcAudioTrack` | `RtcAudioTrackBridge` | `webrtc::AudioTrackInterface` |
| `IRtcVideoSource` | `RtcVideoSourceBridge` | `webrtc::VideoTrackSourceInterface` |
| `IRtcAudioSource` | `RtcAudioSourceBridge` | `webrtc::AudioSourceInterface` |
| `IRtcMediaStream` | `RtcMediaStreamBridge` | `webrtc::MediaStreamInterface` |
| `IRtcRtpTransceiver` | `RtcRtpTransceiverBridge` | `webrtc::RtpTransceiverInterface` |
| `IRtcRtpSender` | `RtcRtpSenderBridge` | `webrtc::RtpSenderInterface` |
| `IRtcVideoSink` | `ManagedVideoSinkAdapter` | `rtc::VideoSinkInterface<VideoFrame>` |
| `IRtcPlatform` | `RtcPlatformImpl` | `rtc::InitializeSSL()` etc. |

## Version

Uses the same `branch-heads/7827` WebRTC branch as the Android client
(`client-android/serenada-core/libs/libwebrtc-7827-universal.aar`).

The checked-in `.vcxproj` targets `net9.0-windows10.0.19041.0` with `CLRSupport=NetCore`,
producing a managed DLL that .NET 9 can reference directly.
