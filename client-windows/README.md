# Serenada for Windows

The Windows client is a fully native C#/WinUI 3 application. Its headless
`SerenadaCore` package owns signaling, reconnect recovery, camera/microphone
capture, and WebRTC negotiation; `SerenadaCallUI` renders native video frames
and call controls.

Supported in the current build:

- native camera and microphone capture;
- native local and remote video rendering;
- mode-based switching between available cameras;
- adaptive calls with up to four participants;
- WebSocket signaling with SSE fallback;
- TURN credential and reconnect-token refresh;
- automatic transport and per-peer media recovery;
- non-blocking native media and peer-connection startup;
- persistent display-name, microphone, camera, server-host, and floating-button preferences;
- named saved rooms with a compact card layout, last-used metadata, join, copy-link, and remove actions;
- an optional always-on-top floating Serenada button for quickly returning to the app;
- rounded Windows 11 window corners with a custom integrated title bar.

The Settings screen also links directly to the Windows microphone and camera
privacy pages. The server picker includes `serenada.app`, `serenada-app.ru`,
and custom hosts; a host is persisted only after its Serenada room endpoint
passes validation. Diagnostic logs are available from Settings and are stored
in `%LOCALAPPDATA%\Serenada\serenada.log`.

Saved rooms are stored locally in
`%LOCALAPPDATA%\Serenada\saved-rooms.json`. A named room link uses
`/call/{roomId}?host={host}&name={roomName}`; pasting it into the Windows
client saves the room instead of joining immediately, matching Android.

Native Windows screen capture is not implemented yet, so the client does not
advertise independent content video and the call UI hides the screen-share
control. This avoids negotiating a capability the executable cannot deliver.

## Build and run

From `client-windows/`:

```powershell
dotnet build SerenadaWindows.sln -c Debug -p:Platform=x64
.\SerenadaApp\bin\x64\Debug\net9.0-windows10.0.26100.0\win-x64\SerenadaApp.exe
```

## Publish portable files

From the repository root:

```powershell
.\client-windows\publish.ps1
```

The self-contained application files are created at
`client-windows\artifacts\SerenadaApp-win-x64\`. No separate .NET runtime is
required, but the entire publish folder must stay together because the native
WebRTC and Windows App SDK files are part of the application.

## Build the single-EXE installer

Install Inno Setup 6 once on the build machine, then run:

```powershell
.\client-windows\build-installer.ps1
```

The result is one distributable file:

```text
client-windows\artifacts\installer\Serenada-Setup-x64.exe
```

The installer is per-user by default, installs Serenada under
`%LOCALAPPDATA%\Programs\Serenada`, creates a Start menu shortcut, offers an
optional desktop shortcut, and can launch Serenada immediately after setup.

GitHub Actions also builds this installer automatically for Windows-client pull
requests and pushes to `main`; download the `Serenada-Windows-x64` artifact
from the workflow run to get the single EXE without installing build tools
locally.
