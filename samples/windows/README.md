# Serenada Windows Sample App

Minimal WinUI 3 host app demonstrating Serenada SDK integration using
`SerenadaCore` and `SerenadaCallUI` directly from this repo.

## What it does

- Accepts a call URL, creates a session via `join()` (URL-first path)
- Creates a new room via `SerenadaCore.CreateRoomAsync()` and joins explicitly
- Starts a provider-mode demo backed by an in-memory `SignalingProvider`
- Shows incremental peer-message delivery without Serenada server transport
- Total integration: ~100 lines of C#

## Build & run

The sample references `SerenadaCore` and `SerenadaCallUI` as local project
dependencies, so no NuGet publishing step is needed.

```bash
cd samples/windows/SerenadaWindowsSample
dotnet build
dotnet run
```

> **Note:** Camera preview requires a physical camera — the WinUI app
> will request camera and microphone permissions on first launch.

## Integration pattern

```csharp
// 1. Initialize core
var serenada = new SerenadaCore(new SerenadaConfig
{
    ServerHost = "serenada.app",
});

// 2. Join via URL
var session = serenada.Join(url: callUrl);

// 3. Present call UI
new SerenadaCallFlow
{
    Session = session,
    Config = new SerenadaCallFlowConfig(),
    OnEndCall = () => session.Leave(),
    OnDismiss = () => session.Dispose(),
};

// Or create a room, then join explicitly
var room = await serenada.CreateRoomAsync();
var session = serenada.Join(url: room.RoomUrl);
```

Provider mode uses the same SDK entry point with a custom `SignalingProvider`:

```csharp
var provider = new SampleMockSignalingProvider();
var providerCore = new SerenadaCore(new SerenadaConfig
{
    SignalingProvider = provider,
});
var session = providerCore.Join(roomId: "provider-demo-room");
session.OnPeerMessage((message) =>
    Console.WriteLine($"provider message: {message.Type}"));
```

## Sample limitations

This sample is intentionally minimal. A full product app would add:
- Navigation between home and call screens
- Push notification support (WNS)
- Foreground call service
- Settings persistence
