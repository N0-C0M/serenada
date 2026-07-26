using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Serenada.Core.Models;
using Serenada.Core.WebRtc;

namespace Serenada.CallUI;

/// <summary>
/// Native WinUI call surface with local preview, adaptive remote participant
/// tiles, call controls, and reconnect/error overlays.
/// </summary>
public sealed partial class CallScreen : UserControl, IDisposable
{
    private readonly CallViewModel _vm;
    private readonly SerenadaCallFlowConfig _config;
    private readonly Action? _endCallAction;
    private readonly Action? _dismissAction;
    private readonly PropertyChangedEventHandler _propertyChangedHandler;
    private readonly VideoFramePresenter _localPresenter;
    private readonly Dictionary<string, RemoteTile> _remoteTiles = [];

    private Action? _unsubscribeLocalVideo;
    private Action? _unsubscribeRemoteVideo;
    private bool _hasLocalTrack;
    private bool _wasActive;
    private bool _localEndRequested;
    private bool _dismissed;
    private bool _disposed;

    public CallScreen(
        CallViewModel viewModel,
        SerenadaCallFlowConfig config,
        Action? endCallAction,
        Action? dismissAction)
    {
        _vm = viewModel;
        _config = config;
        _endCallAction = endCallAction;
        _dismissAction = dismissAction;

        InitializeComponent();

        _propertyChangedHandler = (_, _) => UpdateUI();
        _vm.PropertyChanged += _propertyChangedHandler;
        _localPresenter = new VideoFramePresenter(LocalVideoImage);
        _unsubscribeLocalVideo =
            _vm.Session.SubscribeLocalVideoTrack(HandleLocalVideoTrack);
        _unsubscribeRemoteVideo =
            _vm.Session.SubscribeRemoteVideoTrack(HandleRemoteVideoTrack);
        Unloaded += OnUnloaded;

        ScreenShareButton.Visibility =
            _config.ScreenSharingEnabled && _vm.Session.CanScreenShare
                ? Visibility.Visible
                : Visibility.Collapsed;
        EndCallButton.Visibility = _config.EndCallEnabled
            ? Visibility.Visible
            : Visibility.Collapsed;
        FlipCameraButton.Visibility = _vm.Session.HasMultipleCameras
            ? Visibility.Visible
            : Visibility.Collapsed;
        RoomTitle.Text = _config.Title;
        UpdateUI();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Unloaded -= OnUnloaded;
        _vm.PropertyChanged -= _propertyChangedHandler;
        _unsubscribeLocalVideo?.Invoke();
        _unsubscribeRemoteVideo?.Invoke();
        _unsubscribeLocalVideo = null;
        _unsubscribeRemoteVideo = null;
        _localPresenter.Dispose();
        foreach (var tile in _remoteTiles.Values)
            tile.Dispose();
        _remoteTiles.Clear();
    }

    private void UpdateUI()
    {
        if (_disposed) return;

        if (!_vm.IsIdle)
            _wasActive = true;

        CallStatusText.Text = _vm.StatusText;
        JoiningSpinner.IsActive = _vm.IsJoining;
        StatusText.Text = _vm.ConnectionStatusText;
        StatusOverlay.Visibility = _vm.IsConnectionDegraded
            ? Visibility.Visible
            : Visibility.Collapsed;
        ErrorOverlay.Visibility = _vm.IsError
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (_vm.IsError)
            ErrorMessage.Text = _vm.ErrorMessage ?? "An error occurred";

        MicButton.Background = _vm.LocalAudioEnabled
            ? MakeBrush(0x4D, 0xFF, 0xFF, 0xFF)
            : MakeBrush(0xFF, 0xEF, 0x44, 0x44);
        MicIcon.Opacity = _vm.LocalAudioEnabled ? 1.0 : 0.65;

        VideoButton.Background = _vm.LocalVideoEnabled
            ? MakeBrush(0x4D, 0xFF, 0xFF, 0xFF)
            : MakeBrush(0xFF, 0xEF, 0x44, 0x44);
        VideoIcon.Opacity = _vm.LocalVideoEnabled ? 1.0 : 0.65;
        LocalVideoPlaceholder.Visibility =
            _vm.LocalVideoEnabled && _hasLocalTrack
                ? Visibility.Collapsed
                : Visibility.Visible;
        LocalVideoImage.Visibility =
            _vm.LocalVideoEnabled && _hasLocalTrack
                ? Visibility.Visible
                : Visibility.Collapsed;

        FlipCameraButton.Visibility = _vm.Session.HasMultipleCameras
            ? Visibility.Visible
            : Visibility.Collapsed;
        ScreenShareButton.Visibility =
            _config.ScreenSharingEnabled && _vm.Session.CanScreenShare
                ? Visibility.Visible
                : Visibility.Collapsed;

        SyncRemoteTiles();
        RemoteVideoPlaceholder.Visibility = _vm.HasRemoteParticipants
            ? Visibility.Collapsed
            : Visibility.Visible;

        if (_vm.IsIdle && _wasActive && !_localEndRequested && !_dismissed)
        {
            _dismissed = true;
            _dismissAction?.Invoke();
        }
    }

    private void SyncRemoteTiles()
    {
        var participants = _vm.RemoteParticipants;
        var activeCids = participants.Select(p => p.Cid).ToHashSet();
        foreach (var staleCid in _remoteTiles.Keys
                     .Where(cid => !activeCids.Contains(cid))
                     .ToList())
        {
            _remoteTiles[staleCid].Dispose();
            _remoteTiles.Remove(staleCid);
        }

        foreach (var participant in participants)
        {
            if (!_remoteTiles.TryGetValue(participant.Cid, out var tile))
            {
                tile = new RemoteTile(participant.Cid);
                _remoteTiles[participant.Cid] = tile;
            }
            tile.Update(participant);
        }

        RemoteTilesHost.Children.Clear();
        RemoteTilesHost.RowDefinitions.Clear();
        RemoteTilesHost.ColumnDefinitions.Clear();
        if (participants.Count == 0)
            return;

        var columns = participants.Count == 1 ? 1 : 2;
        var rows = (int)Math.Ceiling(participants.Count / (double)columns);
        for (var row = 0; row < rows; row++)
            RemoteTilesHost.RowDefinitions.Add(new RowDefinition());
        for (var column = 0; column < columns; column++)
            RemoteTilesHost.ColumnDefinitions.Add(new ColumnDefinition());

        for (var index = 0; index < participants.Count; index++)
        {
            var tile = _remoteTiles[participants[index].Cid];
            Grid.SetRow(tile.Root, index / columns);
            Grid.SetColumn(tile.Root, index % columns);
            RemoteTilesHost.Children.Add(tile.Root);
        }
    }

    private void HandleLocalVideoTrack(IRtcVideoTrack? track)
    {
        _hasLocalTrack = track != null;
        _localPresenter.SetTrack(track);
        UpdateUI();
    }

    private void HandleRemoteVideoTrack(string cid, IRtcVideoTrack? track)
    {
        if (!_remoteTiles.TryGetValue(cid, out var tile))
        {
            SyncRemoteTiles();
            _remoteTiles.TryGetValue(cid, out tile);
        }
        tile?.SetTrack(track);
    }

    private void OnMicToggle(object sender, RoutedEventArgs e)
    {
        _vm.ToggleAudio();
    }

    private void OnVideoToggle(object sender, RoutedEventArgs e)
    {
        _vm.ToggleVideo();
    }

    private void OnEndCall(object sender, RoutedEventArgs e)
    {
        _localEndRequested = true;
        _vm.Leave();
        _endCallAction?.Invoke();
    }

    private void OnFlipCamera(object sender, RoutedEventArgs e)
    {
        _vm.FlipCamera();
    }

    private void OnScreenShare(object sender, RoutedEventArgs e)
    {
        _vm.StartScreenShare();
    }

    private void OnDismissClick(object sender, RoutedEventArgs e)
    {
        if (_dismissed) return;
        _dismissed = true;
        _dismissAction?.Invoke();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Dispose();
    }

    private static SolidColorBrush MakeBrush(byte a, byte r, byte g, byte b)
    {
        return new SolidColorBrush(
            Microsoft.UI.ColorHelper.FromArgb(a, r, g, b));
    }

    private sealed class RemoteTile : IDisposable
    {
        private readonly Image _image;
        private readonly Grid _placeholder;
        private readonly TextBlock _name;
        private readonly VideoFramePresenter _presenter;
        private bool _hasTrack;
        private bool _videoEnabled;

        public Grid Root { get; }

        public RemoteTile(string cid)
        {
            _image = new Image { Stretch = Stretch.UniformToFill };
            _name = new TextBlock
            {
                Text = cid,
                Foreground = new SolidColorBrush(
                    Microsoft.UI.ColorHelper.FromArgb(0xFF, 0xF8, 0xFA, 0xFC)),
                FontSize = 14,
                Margin = new Thickness(12, 8, 12, 8),
            };
            _placeholder = new Grid
            {
                Background = new SolidColorBrush(
                    Microsoft.UI.ColorHelper.FromArgb(0xFF, 0x1E, 0x29, 0x3B)),
            };
            _placeholder.Children.Add(new FontIcon
            {
                Glyph = "\uE714",
                FontSize = 52,
                Foreground = new SolidColorBrush(
                    Microsoft.UI.ColorHelper.FromArgb(0xFF, 0x47, 0x55, 0x69)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            });

            var nameBackground = new Border
            {
                Background = new SolidColorBrush(
                    Microsoft.UI.ColorHelper.FromArgb(0x99, 0x0F, 0x17, 0x2A)),
                CornerRadius = new CornerRadius(8),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(12),
                Child = _name,
            };

            Root = new Grid
            {
                Background = new SolidColorBrush(
                    Microsoft.UI.ColorHelper.FromArgb(0xFF, 0x1E, 0x29, 0x3B)),
                Margin = new Thickness(4),
            };
            Root.Children.Add(_image);
            Root.Children.Add(_placeholder);
            Root.Children.Add(nameBackground);
            _presenter = new VideoFramePresenter(_image);
        }

        public void Update(RemoteParticipant participant)
        {
            _videoEnabled = participant.VideoEnabled;
            _name.Text = string.IsNullOrWhiteSpace(participant.DisplayName)
                ? "Participant"
                : participant.DisplayName;
            _placeholder.Visibility =
                _videoEnabled && _hasTrack
                    ? Visibility.Collapsed
                    : Visibility.Visible;
            _image.Visibility =
                _videoEnabled && _hasTrack
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            Root.Opacity = participant.PresumedLost ? 0.55 : 1.0;
        }

        public void SetTrack(IRtcVideoTrack? track)
        {
            _hasTrack = track != null;
            _presenter.SetTrack(track);
            _placeholder.Visibility = _videoEnabled && _hasTrack
                ? Visibility.Collapsed
                : Visibility.Visible;
            _image.Visibility = _videoEnabled && _hasTrack
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        public void Dispose()
        {
            _presenter.Dispose();
        }
    }
}
