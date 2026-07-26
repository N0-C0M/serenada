using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Serenada.CallUI;
using Serenada.Core;
using Serenada.Core.Models;
using System.Diagnostics;
using Windows.ApplicationModel.DataTransfer;

namespace SerenadaApp;

/// <summary>
/// Native Windows host for the headless Serenada SDK and WinUI call surface.
/// </summary>
public sealed partial class MainWindow : Window
{
    private SerenadaCore _serenada;
    private AppSettings _settings;
    private readonly SavedRoomStore _savedRoomStore = new();
    private SerenadaSession? _currentSession;

    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        Closed += (_, _) => EndActiveSession();

        _settings = NormalizeSettings(AppSettings.Load());
        _serenada = CreateCore(_settings);
        RenderSavedRooms();

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

        var target = HostUtilities.ParseRoomTarget(url);
        if (target == null)
        {
            StatusLabel.Text = "The call link or room ID is invalid.";
            return;
        }

        if (target.SavedRoomName is { } roomName)
        {
            try
            {
                SaveRoom(target, roomName);
                RoomUrlInput.Text = string.Empty;
                SavedRoomNameInput.Text = string.Empty;
                StatusLabel.Text =
                    $"“{roomName}” was added to saved rooms.";
            }
            catch (Exception ex)
            {
                StatusLabel.Text =
                    $"Could not save the room: {ex.Message}";
            }
            return;
        }

        JoinRoom(target);
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        DisplayNameInput.Text = _settings.DisplayName;
        ServerHostInput.SelectedIndex =
            string.Equals(
                _settings.ServerHost,
                HostUtilities.DefaultHost,
                StringComparison.OrdinalIgnoreCase)
                ? 0
                : string.Equals(
                    _settings.ServerHost,
                    HostUtilities.RussiaHost,
                    StringComparison.OrdinalIgnoreCase)
                    ? 1
                    : -1;
        ServerHostInput.Text = _settings.ServerHost;
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

    private async void OnSettingsSaveClick(object sender, RoutedEventArgs e)
    {
        SettingsSaveButton.IsEnabled = false;
        try
        {
            var serverHost = HostUtilities.NormalizeHost(
                ReadServerHostInput());
            if (serverHost == null)
            {
                SettingsStatusLabel.Text =
                    "Enter a valid server hostname without a path or query.";
                return;
            }

            SettingsStatusLabel.Text = "Checking the server...";
            if (!await _serenada.ValidateServerHostAsync(serverHost))
            {
                SettingsStatusLabel.Text =
                    "This host is unavailable or is not a Serenada server.";
                return;
            }

            var settings = new AppSettings
            {
                DisplayName = DisplayNameInput.Text?.Trim() ?? string.Empty,
                ServerHost = serverHost,
                StartWithMicrophone = StartWithMicrophoneToggle.IsOn,
                StartWithCamera = StartWithCameraToggle.IsOn,
            };
            settings.Save();
            _settings = settings;
            _serenada = CreateCore(_settings);
            SettingsPanel.Visibility = Visibility.Collapsed;
            HomePanel.Visibility = Visibility.Visible;
            StatusLabel.Text =
                $"Settings saved. Server: {_settings.ServerHost}.";
        }
        catch (Exception ex)
        {
            SettingsStatusLabel.Text = $"Could not save settings: {ex.Message}";
        }
        finally
        {
            SettingsSaveButton.IsEnabled = true;
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
            JoinRoom(
                new RoomTarget(
                    room.RoomId,
                    _settings.ServerHost));
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

    private void OnSaveRoomLinkClick(object sender, RoutedEventArgs e)
    {
        var target = HostUtilities.ParseRoomTarget(RoomUrlInput.Text ?? string.Empty);
        var roomName = HostUtilities.NormalizeRoomName(
            SavedRoomNameInput.Text)
            ?? target?.SavedRoomName;
        if (target == null)
        {
            StatusLabel.Text =
                "Paste a valid call link or room ID before saving.";
            return;
        }
        if (roomName == null)
        {
            StatusLabel.Text = "Enter a name for the saved room.";
            return;
        }

        try
        {
            SaveRoom(target, roomName);
            SavedRoomNameInput.Text = string.Empty;
            StatusLabel.Text = $"“{roomName}” was saved.";
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"Could not save the room: {ex.Message}";
        }
    }

    private async void OnCreateSavedRoomClick(
        object sender,
        RoutedEventArgs e)
    {
        var roomName = HostUtilities.NormalizeRoomName(
            SavedRoomNameInput.Text);
        if (roomName == null)
        {
            StatusLabel.Text = "Enter a name for the new room.";
            return;
        }

        CreateSavedRoomButton.IsEnabled = false;
        try
        {
            StatusLabel.Text = "Creating saved room...";
            var created = await _serenada.CreateRoomAsync();
            var room = new SavedRoom
            {
                RoomId = created.RoomId,
                Name = roomName,
                Host = _settings.ServerHost,
                CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            };
            _savedRoomStore.Save(room);
            RenderSavedRooms();
            var inviteLink = HostUtilities.BuildSavedRoomInviteLink(room);
            RoomUrlInput.Text = inviteLink;
            SavedRoomNameInput.Text = string.Empty;
            StatusLabel.Text = TryCopyText(inviteLink)
                ? $"“{roomName}” was created. Its save link was copied."
                : $"“{roomName}” was created, but the clipboard is unavailable.";
        }
        catch (Exception ex)
        {
            StatusLabel.Text =
                $"Could not create the saved room: {ex.Message}";
        }
        finally
        {
            CreateSavedRoomButton.IsEnabled = true;
        }
    }

    private void JoinCall(string roomId, RecoveryRecord? recovery = null)
    {
        try
        {
            EndActiveSession();
            CallPanel.Children.Clear();

            _currentSession = recovery == null
                ? _serenada.Join(
                    roomId: roomId,
                    displayName: DisplayNameOrNull())
                : _serenada.Rejoin(
                    recovery,
                    displayName: DisplayNameOrNull());

            ShowCall(_currentSession);
        }
        catch (Exception ex)
        {
            ShowJoinError(ex);
        }
    }

    private void JoinRoom(RoomTarget target)
    {
        try
        {
            var host = HostUtilities.NormalizeHost(target.Host)
                ?? _settings.ServerHost;
            var core = string.Equals(
                host,
                _settings.ServerHost,
                StringComparison.OrdinalIgnoreCase)
                    ? _serenada
                    : CreateCore(_settings, host);

            EndActiveSession();
            CallPanel.Children.Clear();
            _currentSession = core.Join(
                roomId: target.RoomId,
                displayName: DisplayNameOrNull());
            _savedRoomStore.MarkJoined(target.RoomId);
            ShowCall(_currentSession);
        }
        catch (Exception ex)
        {
            ShowJoinError(ex);
        }
    }

    private void ShowCall(SerenadaSession session)
    {
        HomePanel.Visibility = Visibility.Collapsed;
        SettingsPanel.Visibility = Visibility.Collapsed;
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
            Session = session,
        };
        CallPanel.Children.Add(callFlow);
    }

    private void ShowJoinError(Exception error)
    {
        EndActiveSession();
        CallPanel.Children.Clear();
        CallPanel.Visibility = Visibility.Collapsed;
        HomePanel.Visibility = Visibility.Visible;
        StatusLabel.Text = $"Could not join the call: {error.Message}";
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
            RenderSavedRooms();
        });
    }

    private void EndActiveSession()
    {
        _currentSession?.Dispose();
        _currentSession = null;
    }

    private static SerenadaCore CreateCore(
        AppSettings settings,
        string? serverHost = null)
    {
        return new SerenadaCore(new SerenadaConfig
        {
            ServerHost = serverHost ?? settings.ServerHost,
            DefaultAudioEnabled = settings.StartWithMicrophone,
            DefaultVideoEnabled = settings.StartWithCamera,
            Logger = FileSerenadaLogger.Instance,
        });
    }

    private void SaveRoom(RoomTarget target, string roomName)
    {
        _savedRoomStore.Save(new SavedRoom
        {
            RoomId = target.RoomId,
            Name = roomName,
            Host = HostUtilities.NormalizeHost(target.Host)
                ?? _settings.ServerHost,
            CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        });
        RenderSavedRooms();
    }

    private void RenderSavedRooms()
    {
        SavedRoomsList.Children.Clear();
        var rooms = _savedRoomStore.Load();
        SavedRoomsEmptyText.Visibility = rooms.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        foreach (var room in rooms)
        {
            var details = new StackPanel { Spacing = 2 };
            details.Children.Add(new TextBlock
            {
                Text = room.Name,
                Foreground = Brush(0xFF, 0xF8, 0xFA, 0xFC),
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            details.Children.Add(new TextBlock
            {
                Text = room.Host,
                Foreground = Brush(0xFF, 0xCB, 0xD5, 0xE1),
                FontSize = 12,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });

            var joinButton = RoomActionButton("Join", "#2563EB");
            joinButton.Click += (_, _) => JoinRoom(
                new RoomTarget(room.RoomId, room.Host));
            var copyButton = RoomActionButton("Copy", "#334155");
            copyButton.Click += (_, _) =>
            {
                StatusLabel.Text = TryCopyText(
                    HostUtilities.BuildSavedRoomInviteLink(room))
                        ? $"Save link for “{room.Name}” copied."
                        : "The clipboard is unavailable.";
            };
            var removeButton = RoomActionButton("Remove", "#7F1D1D");
            removeButton.Click += (_, _) =>
            {
                try
                {
                    _savedRoomStore.Remove(room.RoomId);
                    RenderSavedRooms();
                    StatusLabel.Text = $"“{room.Name}” was removed.";
                }
                catch (Exception ex)
                {
                    StatusLabel.Text =
                        $"Could not remove the room: {ex.Message}";
                }
            };

            var actions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            actions.Children.Add(joinButton);
            actions.Children.Add(copyButton);
            actions.Children.Add(removeButton);

            var grid = new Grid { ColumnSpacing = 12 };
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = GridLength.Auto,
            });
            Grid.SetColumn(actions, 1);
            grid.Children.Add(details);
            grid.Children.Add(actions);

            SavedRoomsList.Children.Add(new Border
            {
                Background = Brush(0xFF, 0x1E, 0x29, 0x3B),
                BorderBrush = Brush(0xFF, 0x64, 0x74, 0x8B),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12),
                Child = grid,
            });
        }
    }

    private static Button RoomActionButton(string text, string background)
    {
        return new Button
        {
            Content = text,
            Background = new SolidColorBrush(
                Microsoft.UI.ColorHelper.FromArgb(
                    0xFF,
                    Convert.ToByte(background.Substring(1, 2), 16),
                    Convert.ToByte(background.Substring(3, 2), 16),
                    Convert.ToByte(background.Substring(5, 2), 16))),
            Foreground = Brush(0xFF, 0xF8, 0xFA, 0xFC),
            BorderBrush = Brush(0xFF, 0x94, 0xA3, 0xB8),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(10, 7, 10, 7),
        };
    }

    private static SolidColorBrush Brush(byte a, byte r, byte g, byte b)
    {
        return new SolidColorBrush(
            Microsoft.UI.ColorHelper.FromArgb(a, r, g, b));
    }

    private static bool TryCopyText(string value)
    {
        try
        {
            var package = new DataPackage();
            package.SetText(value);
            Clipboard.SetContent(package);
            Clipboard.Flush();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private string ReadServerHostInput()
    {
        if (!string.IsNullOrWhiteSpace(ServerHostInput.Text))
            return ServerHostInput.Text;
        return (ServerHostInput.SelectedItem as ComboBoxItem)?.Content?.ToString()
            ?? string.Empty;
    }

    private static AppSettings NormalizeSettings(AppSettings settings)
    {
        return settings with
        {
            ServerHost = HostUtilities.NormalizeHost(settings.ServerHost)
                ?? HostUtilities.DefaultHost,
        };
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
