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
- persistent display-name, microphone, camera, and server-host preferences;
- named saved rooms with join, copy-link, and remove actions.

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

## Publish a launchable executable

From the repository root, run:

```powershell
.\client-windows\publish.ps1
```

The self-contained executable is created at
`client-windows\artifacts\SerenadaApp-win-x64\SerenadaApp.exe`. Copy the whole
`SerenadaApp-win-x64` folder to another 64-bit Windows computer, then start the
`.exe`; no .NET installation is required. To build the 32-bit variant, pass
`-Architecture x86`. The current native WebRTC package supports x64 and x86.
