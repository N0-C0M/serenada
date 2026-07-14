using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Serenada.Core;
using Serenada.CallUI;

namespace SerenadaApp;

/// <summary>
/// Main application window. Demonstrates Serenada SDK integration with
/// URL-first and session-first join patterns — mirrors the Android
/// and iOS sample apps (~80 lines of integration code).
/// </summary>
public sealed partial class MainWindow : Window
{
    private SerenadaCore? _serenada;
    private SerenadaSession? _currentSession;

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;

        _serenada = new SerenadaCore(new SerenadaConfig
        {
            ServerHost = "serenada.app",
        });

        // Check for recoverable session (crash recovery)
        var recovery = _serenada.GetRecoverableSession();
        if (recovery != null)
        {
            StatusLabel.Text = "You have an active call. Rejoining…";
            JoinCall(recovery.RoomId);
        }
    }

    // ── Home screen actions ──────────────────────────────────

    private async void OnJoinClick(object sender, RoutedEventArgs e)
    {
        var url = RoomUrlInput.Text?.Trim();
        if (string.IsNullOrWhiteSpace(url))
        {
            StatusLabel.Text = "Please paste a call link.";
            return;
        }

        JoinCall(url);
    }

    private async void OnCreateRoomClick(object sender, RoutedEventArgs e)
    {
        if (_serenada == null) return;

        try
        {
            StatusLabel.Text = "Creating room…";
            CreateRoomButton.IsEnabled = false;

            var room = await _serenada.CreateRoomAsync();

            // Show the room URL (user can share it)
            RoomUrlInput.Text = room.RoomUrl;
            StatusLabel.Text = $"Room created. Share the link, then join.";

            // Join the room
            JoinCall(room.RoomUrl);
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"Error: {ex.Message}";
        }
        finally
        {
            CreateRoomButton.IsEnabled = true;
        }
    }

    // ── Call flow ────────────────────────────────────────────

    private void JoinCall(string url)
    {
        if (_serenada == null) return;

        // Clean up previous session
        _currentSession?.Dispose();

        // Create the session
        _currentSession = _serenada.Join(url: url);

        // Switch to call UI
        HomePanel.Visibility = Visibility.Collapsed;
        CallPanel.Visibility = Visibility.Visible;

        // Render the pre-built call flow component
        var callFlow = new SerenadaCallFlow
        {
            Session = _currentSession,
            Config = new SerenadaCallFlowConfig
            {
                Title = "Serenada",
                ScreenSharingEnabled = true,
                InviteControlsEnabled = false,
                EndCallEnabled = true,
            },
            OnEndCall = () =>
            {
                // Return to home screen
                DispatcherQueue.TryEnqueue(() =>
                {
                    CallPanel.Visibility = Visibility.Collapsed;
                    CallPanel.Children.Clear();
                    HomePanel.Visibility = Visibility.Visible;
                    StatusLabel.Text = "Call ended.";
                });
            },
            OnDismiss = () =>
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    CallPanel.Visibility = Visibility.Collapsed;
                    CallPanel.Children.Clear();
                    HomePanel.Visibility = Visibility.Visible;
                    _currentSession?.Dispose();
                    _currentSession = null;
                });
            },
        };

        CallPanel.Children.Add(callFlow);
    }
}
