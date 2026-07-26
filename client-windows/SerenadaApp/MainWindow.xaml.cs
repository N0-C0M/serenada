using Microsoft.UI.Xaml;
using Serenada.CallUI;
using Serenada.Core;
using Serenada.Core.Models;

namespace SerenadaApp;

/// <summary>
/// Native Windows host for the headless Serenada SDK and WinUI call surface.
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly SerenadaCore _serenada;
    private SerenadaSession? _currentSession;

    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        Closed += (_, _) => EndActiveSession();

        _serenada = new SerenadaCore(new SerenadaConfig
        {
            ServerHost = "serenada.app",
        });

        var recovery = _serenada.GetRecoverableSession();
        if (recovery != null)
        {
            StatusLabel.Text = "Rejoining your active call...";
            JoinCall(recovery.RoomId, recovery);
        }
    }

    private void OnJoinClick(object sender, RoutedEventArgs e)
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
        try
        {
            StatusLabel.Text = "Creating room...";
            CreateRoomButton.IsEnabled = false;
            JoinButton.IsEnabled = false;

            var room = await _serenada.CreateRoomAsync();
            RoomUrlInput.Text = room.RoomUrl;
            JoinCall(room.RoomUrl);
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"Could not create the room: {ex.Message}";
        }
        finally
        {
            CreateRoomButton.IsEnabled = true;
            JoinButton.IsEnabled = true;
        }
    }

    private void JoinCall(string url, RecoveryRecord? recovery = null)
    {
        try
        {
            EndActiveSession();
            CallPanel.Children.Clear();

            _currentSession = recovery == null
                ? _serenada.Join(url: url)
                : _serenada.Rejoin(recovery);

            HomePanel.Visibility = Visibility.Collapsed;
            CallPanel.Visibility = Visibility.Visible;

            var callFlow = new SerenadaCallFlow
            {
                Config = new SerenadaCallFlowConfig
                {
                    Title = "Serenada",
                    ScreenSharingEnabled = false,
                    InviteControlsEnabled = false,
                    EndCallEnabled = true,
                },
                OnEndCall = ReturnHomeAfterCall,
                OnDismiss = ReturnHomeAfterCall,
                Session = _currentSession,
            };
            CallPanel.Children.Add(callFlow);
        }
        catch (Exception ex)
        {
            EndActiveSession();
            CallPanel.Children.Clear();
            CallPanel.Visibility = Visibility.Collapsed;
            HomePanel.Visibility = Visibility.Visible;
            StatusLabel.Text = $"Could not join the call: {ex.Message}";
        }
    }

    private void ReturnHomeAfterCall()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            CallPanel.Visibility = Visibility.Collapsed;
            CallPanel.Children.Clear();
            EndActiveSession();
            HomePanel.Visibility = Visibility.Visible;
            StatusLabel.Text = "Call ended.";
        });
    }

    private void EndActiveSession()
    {
        _currentSession?.Dispose();
        _currentSession = null;
    }
}
