using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Serenada.CallUI;
using Serenada.Core;
using Serenada.Core.Models;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;

namespace SerenadaApp;

/// <summary>
/// Native Windows host for the headless Serenada SDK and WinUI call surface.
/// </summary>
public sealed partial class MainWindow : Window
{
    private const int SwRestore = 9;

    private SerenadaCore _serenada;
    private AppSettings _settings;
    private readonly SavedRoomStore _savedRoomStore = new();
    private SerenadaSession? _currentSession;
    private FloatingBubbleWindow? _floatingBubble;

    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        ConfigureWindowChrome();

        Closed += (_, _) =>
        {
            _floatingBubble?.Close();
            _floatingBubble = null;
            EndActiveSession();
        };

        _settings = NormalizeSettings(AppSettings.Load());
        _serenada = CreateCore(_settings);
        RenderSavedRooms();

        DispatcherQueue.TryEnqueue(ApplyFloatingBubbleSetting);

        var recovery = _serenada.GetRecoverableSession();
        if (recovery != null)
        {
            StatusLabel.Text = "Rejoining your active call...";
            JoinCall(recovery.RoomId, recovery);
        }
    }

    private void ConfigureWindowChrome()
    {
        try
        {
            AppWindow.Resize(new SizeInt32(1080, 760));
            AppWindow.TitleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
            AppWindow.TitleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
            AppWindow.TitleBar.ButtonForegroundColor = Microsoft.UI.Colors.White;
            AppWindow.TitleBar.ButtonInactiveForegroundColor =
                Microsoft.UI.ColorHelper.FromArgb(0xFF, 0x7F, 0x8D, 0xA3);
        }
        catch
        {
            // Window sizing and title bar colors are cosmetic.
        }

        Activated += (_, _) => WindowChrome.PreferRoundedCorners(this);
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
        FloatingBubbleToggle.IsOn = _settings.FloatingBubbleEnabled;
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
                FloatingBubbleEnabled = FloatingBubbleToggle.IsOn,
            };
            settings.Save();
            _settings = settings;
            _serenada = CreateCore(_settings);
            ApplyFloatingBubbleSetting();

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

    private void ApplyFloatingBubbleSetting()
    {
        if (!_settings.FloatingBubbleEnabled)
        {
            _floatingBubble?.Close();
            _floatingBubble = null;
            return;
        }

        _floatingBubble ??= new FloatingBubbleWindow(ActivateMainWindow);
        _floatingBubble.ShowBubble();

        // Opening the helper window should not take the user away from Serenada.
        Activate();
    }

    private void ActivateMainWindow()
    {
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            _ = ShowWindow(hwnd, SwRestore);
            _ = SetForegroundWindow(hwnd);
        }
        catch
        {
            // Activate below is still enough on systems where the Win32 call fails.
        }

        Activate();
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
                DiagnosticLog = message => FileSerenadaLogger.Instance.Log(
                    SerenadaLogLevel.Info,
                    "CallUI",
                    message),
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
        SavedRoomsCountText.Text = rooms.Count == 1
            ? "1 room"
            : $"{rooms.Count} rooms";

        foreach (var room in rooms)
        {
            var avatar = new Border
            {
                Width = 42,
                Height = 42,
                CornerRadius = new CornerRadius(14),
                Background = Brush(0xFF, 0x14, 0x21, 0x3A),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = room.Name[..1].ToUpperInvariant(),
                    FontSize = 16,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = Brush(0xFF, 0xBF, 0xDB, 0xFE),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };

            var details = new StackPanel
            {
                Spacing = 3,
                VerticalAlignment = VerticalAlignment.Center,
            };
            details.Children.Add(new TextBlock
            {
                Text = room.Name,
                Foreground = Brush(0xFF, 0xF8, 0xFA, 0xFC),
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            details.Children.Add(new TextBlock
            {
                Text = $"{room.Host}  •  {FormatRoomActivity(room)}",
                Foreground = Brush(0xFF, 0x7F, 0x91, 0xAA),
                FontSize = 11,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });

            var joinButton = new Button
            {
                Content = "Join",
                MinWidth = 68,
                Height = 36,
                Background = Brush(0xFF, 0x1D, 0x4E, 0xD8),
                Foreground = Brush(0xFF, 0xFF, 0xFF, 0xFF),
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 7, 14, 7),
            };
            joinButton.Click += (_, _) => JoinRoom(
                new RoomTarget(room.RoomId, room.Host));

            var moreButton = new Button
            {
                Content = "•••",
                Width = 38,
                Height = 36,
                Padding = new Thickness(0),
                Background = Brush(0xFF, 0x17, 0x22, 0x35),
                Foreground = Brush(0xFF, 0xC7, 0xD2, 0xE1),
                BorderBrush = Brush(0xFF, 0x2B, 0x3A, 0x51),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                FontSize = 14,
            };
            ToolTipService.SetToolTip(moreButton, "Room actions");

            var menu = new MenuFlyout();
            var copyItem = new MenuFlyoutItem
            {
                Text = "Copy save link",
            };
            copyItem.Click += (_, _) =>
            {
                StatusLabel.Text = TryCopyText(
                    HostUtilities.BuildSavedRoomInviteLink(room))
                        ? $"Save link for “{room.Name}” copied."
                        : "The clipboard is unavailable.";
            };

            var removeItem = new MenuFlyoutItem
            {
                Text = "Remove room",
            };
            removeItem.Click += (_, _) =>
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

            menu.Items.Add(copyItem);
            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(removeItem);
            moreButton.Flyout = menu;

            var actions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 7,
                VerticalAlignment = VerticalAlignment.Center,
            };
            actions.Children.Add(joinButton);
            actions.Children.Add(moreButton);

            var grid = new Grid
            {
                ColumnSpacing = 12,
            };
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = GridLength.Auto,
            });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = GridLength.Auto,
            });

            Grid.SetColumn(details, 1);
            Grid.SetColumn(actions, 2);
            grid.Children.Add(avatar);
            grid.Children.Add(details);
            grid.Children.Add(actions);

            SavedRoomsList.Children.Add(new Border
            {
                Background = Brush(0xFF, 0x10, 0x19, 0x2A),
                BorderBrush = Brush(0xFF, 0x20, 0x2E, 0x45),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(13),
                Padding = new Thickness(12),
                Child = grid,
            });
        }
    }

    private static string FormatRoomActivity(SavedRoom room)
    {
        if (room.LastJoinedAt is not > 0)
            return "Not joined yet";

        try
        {
            var joined = DateTimeOffset
                .FromUnixTimeMilliseconds(room.LastJoinedAt.Value)
                .ToLocalTime();
            var now = DateTimeOffset.Now;

            if (joined.Date == now.Date)
                return $"Used today {joined:HH:mm}";
            if (joined.Date == now.Date.AddDays(-1))
                return $"Used yesterday {joined:HH:mm}";

            return $"Used {joined:MMM d}";
        }
        catch
        {
            return "Previously used";
        }
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

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint hWnd);
}
