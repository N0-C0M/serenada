using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Serenada.Core.Models;

namespace Serenada.CallUI;

/// <summary>
/// Main call screen — renders the in-call UI with remote video, local PiP,
/// call controls, and status overlays. Data-bound to <see cref="CallViewModel"/>.
/// </summary>
public sealed partial class CallScreen : UserControl
{
    private readonly CallViewModel _vm;

    internal SerenadaCallFlowConfig? FlowConfig { get; set; }
    internal Action? EndCallAction { get; set; }
    internal Action? DismissAction { get; set; }

    public CallScreen(CallViewModel viewModel)
    {
        _vm = viewModel;
        InitializeComponent();

        // Bind ViewModel events
        _vm.PropertyChanged += (_, _) => UpdateUI();
        UpdateUI();

        // Apply config
        ScreenShareButton.Visibility = FlowConfig?.ScreenSharingEnabled == true
            ? Visibility.Visible : Visibility.Collapsed;
        EndCallButton.Visibility = FlowConfig?.EndCallEnabled == true
            ? Visibility.Visible : Visibility.Collapsed;
        RoomTitle.Text = FlowConfig?.Title ?? "Serenada";
    }

    private void UpdateUI()
    {
        // Status text
        CallStatusText.Text = _vm.StatusText;
        JoiningSpinner.IsActive = _vm.IsJoining;
        StatusOverlay.Visibility = _vm.IsConnectionDegraded
            ? Visibility.Visible : Visibility.Collapsed;
        ErrorOverlay.Visibility = _vm.IsError
            ? Visibility.Visible : Visibility.Collapsed;

        if (_vm.IsError)
        {
            ErrorMessage.Text = _vm.ErrorMessage ?? "An error occurred";
        }

        // Mic button
        MicIcon.Glyph = _vm.LocalAudioEnabled ? "" : "";
        MicButton.Background = _vm.LocalAudioEnabled
            ? MakeBrush(Microsoft.UI.ColorHelper.FromArgb(0x4D, 0xFF, 0xFF, 0xFF))
            : MakeBrush(0x33, 0x41, 0x55);

        // Video button
        VideoIcon.Glyph = _vm.LocalVideoEnabled ? "" : "";
        VideoButton.Background = _vm.LocalVideoEnabled
            ? MakeBrush(Microsoft.UI.ColorHelper.FromArgb(0x4D, 0xFF, 0xFF, 0xFF))
            : MakeBrush(0x33, 0x41, 0x55);

        // Local video PiP placeholder
        LocalVideoPlaceholder.Visibility = _vm.LocalVideoEnabled
            ? Visibility.Collapsed : Visibility.Visible;

        // Remote participants
        if (_vm.HasRemoteParticipants)
        {
            var participant = _vm.RemoteParticipants[0];
            CallStatusText.Text = participant.DisplayName ?? "In call";
            RemoteVideoPlaceholder.Visibility = participant.VideoEnabled
                ? Visibility.Collapsed : Visibility.Visible;

            // Show participant count
            if (_vm.ParticipantCount > 2)
            {
                CallStatusText.Text += $" +{_vm.ParticipantCount - 1}";
            }
        }
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
        _vm.Leave();
        EndCallAction?.Invoke();
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
        DismissAction?.Invoke();
    }

    private static Microsoft.UI.Xaml.Media.SolidColorBrush MakeBrush(byte r, byte g, byte b)
    {
        return new Microsoft.UI.Xaml.Media.SolidColorBrush(
            Microsoft.UI.ColorHelper.FromArgb(0xFF, r, g, b));
    }

    private static Microsoft.UI.Xaml.Media.SolidColorBrush MakeBrush(Windows.UI.Color color)
    {
        return new Microsoft.UI.Xaml.Media.SolidColorBrush(color);
    }
}
