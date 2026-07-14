using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Serenada.Core;
using Serenada.Core.Signaling;

namespace Serenada.Windows.Sample;

/// <summary>
/// Minimal sample app demonstrating Serenada SDK integration.
/// Mirrors the ~80-line Android and iOS samples.
/// </summary>
public sealed partial class MainWindow : Window
{
    private SerenadaCore? _serenada;

    public MainWindow()
    {
        InitializeComponent();

        // Set window size (WinUI doesn't support Width/Height in XAML)
        AppWindow.Resize(new global::Windows.Graphics.SizeInt32(420, 520));

        // 1. Initialize core with a server host
        _serenada = new SerenadaCore(new SerenadaConfig
        {
            ServerHost = "serenada.app",
        });
    }

    // 2a. Join an existing invite link by URL
    private void OnJoinUrlClick(object sender, RoutedEventArgs e)
    {
        var url = UrlInput.Text?.Trim();
        if (string.IsNullOrWhiteSpace(url))
        {
            _ = ShowDialogAsync("Error", "Please paste a call URL.");
            return;
        }

        var session = _serenada!.Join(url: url);

        PresentCall(session);
    }

    // 2b. Create a room, then join explicitly
    private async void OnCreateRoomClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var room = await _serenada!.CreateRoomAsync();

            // room.roomUrl is the link you share with the other participant
            var session = _serenada.Join(url: room.RoomUrl);

            PresentCall(session);
        }
        catch (Exception ex)
        {
            await ShowDialogAsync("Error", ex.Message);
        }
    }

    // Provider mode: inject a custom signaling provider.
    private void OnProviderDemoClick(object sender, RoutedEventArgs e)
    {
        var provider = new SampleMockSignalingProvider();

        var providerCore = new SerenadaCore(new SerenadaConfig
        {
            SignalingProvider = provider,
        });

        var session = providerCore.Join(roomId: "provider-demo-room");
        session.OnPeerMessage((message) =>
        {
            System.Diagnostics.Debug.WriteLine(
                $"Provider message: {message.Type} from {message.From}");
        });

        _ = ShowDialogAsync("Provider Demo",
            $"Connected with custom signaling provider.\n" +
            $"Session initialized for room: {session.RoomId}");
    }

    // ── Present call UI ──────────────────────────────────────

    private void PresentCall(SerenadaSession session)
    {
        // In a real app, you'd navigate to a call page.
        // For the sample, just show a dialog with session info.
        _ = ShowDialogAsync("Call Started",
            $"Room: {session.RoomId}\n" +
            $"Phase: {session.State.Phase}\n\n" +
            "Call UI would be shown here via SerenadaCallFlow.");
    }

    private async Task ShowDialogAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = "OK",
            XamlRoot = Content.XamlRoot,
        };
        await dialog.ShowAsync();
    }
}

/// <summary>
/// Minimal custom signaling provider for demo/testing.
/// Mirrors the in-memory provider pattern from the Android and Web samples.
/// </summary>
internal class SampleMockSignalingProvider : SignalingProviderBase
{
    public override int Version => SupportedVersion;
    public override ProviderCapabilities Capabilities => new() { HandlesReconnection = false };

    public override void Connect()
    {
        RaiseConnected(new ConnectionInfo { Transport = "mock" });
    }

    public override void Disconnect() { }

    public override void JoinRoom(string roomId, JoinOptions options)
    {
        RaiseJoined(new JoinedPayload
        {
            HostCid = "sample-host",
            Participants = [
                new SignalingParticipant
                {
                    Cid = "sample-host",
                    JoinedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    AudioEnabled = true,
                    VideoEnabled = true,
                }
            ],
            MaxParticipants = 2,
        });
    }

    public override void LeaveRoom() { }
    public override void EndRoom() { }
    public override void SendToPeer(string peerId, string type, object? payload) { }
    public override void Broadcast(string type, object? payload) { }
    public override Task<IReadOnlyList<IceServer>> GetIceServersAsync()
        => Task.FromResult<IReadOnlyList<IceServer>>([]);
}
