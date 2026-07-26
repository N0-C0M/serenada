using Microsoft.UI.Xaml;
using Serenada.CallUI;
using Serenada.Core;
using Serenada.Core.Models;
using System.Diagnostics;

namespace SerenadaApp;

/// <summary>
/// Native Windows host for the headless Serenada SDK and WinUI call surface.
/// </summary>
public sealed partial class MainWindow : Window
{
    private SerenadaCore _serenada;
    private AppSettings _settings;
    private SerenadaSession? _currentSession;

    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        Closed += (_, _) => EndActiveSession();

        _settings = AppSettings.Load();
        _serenada = CreateCore(_settings);

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

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        DisplayNameInput.Text = _settings.DisplayName;
        StartWithMicrophoneToggle.IsOn = _settings.StartWithMicrophone;
        StartWithCameraToggle.IsOn = _settings.StartWithCamera;
        SettingsStatusLabel.Text = string.Empty;
        HomePanel.Visibility = Visibility.Collapsed;
        SettingsPanel.Visibility = Visibility.Visible;
    }

    private void OnSettingsCancelClick(object sender, RoutedEventArgs e)
    {
        SettingsPanel.Visibility = Visibility.Collapsed;
        HomePanel.Visibility = Visibility.Visible;
    }

    private void OnSettingsSaveClick(object sender, RoutedEventArgs e)
    {
        try
        {
            _settings = new AppSettings
            {
                DisplayName = DisplayNameInput.Text?.Trim() ?? string.Empty,
                StartWithMicrophone = StartWithMicrophoneToggle.IsOn,
                StartWithCamera = StartWithCameraToggle.IsOn,
            };
            _settings.Save();
            _serenada = CreateCore(_settings);
            SettingsPanel.Visibility = Visibility.Collapsed;
            HomePanel.Visibility = Visibility.Visible;
            StatusLabel.Text = "Settings saved.";
        }
        catch (Exception ex)
        {
            SettingsStatusLabel.Text = $"Could not save settings: {ex.Message}";
        }
    }

    private async void OnMicrophonePrivacyClick(object sender, RoutedEventArgs e)
    {
        await OpenPrivacySettingsAsync("ms-settings:privacy-microphone");
    }

    private async void OnCameraPrivacyClick(object sender, RoutedEventArgs e)
    {
        await OpenPrivacySettingsAsync("ms-settings:privacy-webcam");
    }

    private void OnOpenLogsClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(FileSerenadaLogger.LogDirectory);
            Process.Start(new ProcessStartInfo
            {
                FileName = FileSerenadaLogger.LogDirectory,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            SettingsStatusLabel.Text =
                $"Could not open the diagnostic folder: {ex.Message}";
        }
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
                ? _serenada.Join(url: url, displayName: DisplayNameOrNull())
                : _serenada.Rejoin(recovery, displayName: DisplayNameOrNull());

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

    private static SerenadaCore CreateCore(AppSettings settings)
    {
        return new SerenadaCore(new SerenadaConfig
        {
            ServerHost = "serenada.app",
            DefaultAudioEnabled = settings.StartWithMicrophone,
            DefaultVideoEnabled = settings.StartWithCamera,
            Logger = FileSerenadaLogger.Instance,
        });
    }

    private string? DisplayNameOrNull()
    {
        return string.IsNullOrWhiteSpace(_settings.DisplayName)
            ? null
            : _settings.DisplayName;
    }

    private async Task OpenPrivacySettingsAsync(string uri)
    {
        try
        {
            var launched = await Windows.System.Launcher.LaunchUriAsync(new Uri(uri));
            if (!launched)
                SettingsStatusLabel.Text = "Windows could not open the privacy settings.";
        }
        catch (Exception ex)
        {
            SettingsStatusLabel.Text =
                $"Could not open the privacy settings: {ex.Message}";
        }
    }
}
