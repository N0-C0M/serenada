using System.ComponentModel;
using System.Runtime.CompilerServices;
using Serenada.Core;
using Serenada.Core.Models;

namespace Serenada.CallUI;

/// <summary>
/// Observable ViewModel bridging <see cref="SerenadaSession"/> state
/// to WinUI 3 XAML data binding. Wraps <see cref="CallState"/> and raises
/// <see cref="INotifyPropertyChanged"/> for bound properties.
/// </summary>
public sealed class CallViewModel : INotifyPropertyChanged
{
    private readonly SerenadaSession _session;
    private Action? _unsubscribe;
    private CallState _state = new();

    public CallViewModel(SerenadaSession session)
    {
        _session = session;
        _unsubscribe = session.Subscribe(OnStateChanged);
    }

    // ── Bound properties ─────────────────────────────────────

    public CallPhase Phase => _state.Phase;
    public string? RoomId => _state.RoomId;
    public string? RoomUrl => _state.RoomUrl;

    public bool IsJoining => Phase is CallPhase.Joining or CallPhase.AwaitingPermissions;
    public bool IsWaiting => Phase == CallPhase.Waiting;
    public bool IsInCall => Phase == CallPhase.InCall;
    public bool IsEnding => Phase == CallPhase.Ending;
    public bool IsError => Phase == CallPhase.Error;
    public bool IsIdle => Phase == CallPhase.Idle;

    public string StatusText => Phase switch
    {
        CallPhase.Idle => "",
        CallPhase.AwaitingPermissions => "Waiting for permissions…",
        CallPhase.Joining => "Joining call…",
        CallPhase.Waiting => "Waiting for someone to join…",
        CallPhase.InCall => "In call",
        CallPhase.Ending => "Call ended",
        CallPhase.Error => _state.Error?.Message ?? "An error occurred",
        _ => "",
    };

    public string ConnectionStatusText => _state.ConnectionStatus switch
    {
        ConnectionStatus.Connected => "",
        ConnectionStatus.Recovering => "Reconnecting…",
        ConnectionStatus.Retrying => "Connection lost. Retrying…",
        ConnectionStatus.Disconnected => "Disconnected",
        _ => "",
    };

    public bool IsConnectionDegraded =>
        _state.ConnectionStatus is ConnectionStatus.Recovering or ConnectionStatus.Retrying;

    // Local participant
    public string? LocalCid => _state.LocalParticipant?.Cid;
    public string? LocalDisplayName => _state.LocalParticipant?.DisplayName;
    public bool LocalAudioEnabled => _state.LocalParticipant?.AudioEnabled ?? false;
    public bool LocalVideoEnabled => _state.LocalParticipant?.VideoEnabled ?? false;
    public bool IsHost => _state.LocalParticipant?.IsHost ?? false;
    public CameraMode LocalCameraMode => _state.LocalParticipant?.CameraMode ?? CameraMode.Selfie;

    public string AudioButtonIcon => LocalAudioEnabled ? "" : ""; // Mic on/off
    public string VideoButtonIcon => LocalVideoEnabled ? "" : ""; // Camera on/off

    // Remote participants
    public IReadOnlyList<RemoteParticipant> RemoteParticipants => _state.RemoteParticipants;
    public bool HasRemoteParticipants => _state.RemoteParticipants.Count > 0;
    public int ParticipantCount => _state.ParticipantCount;

    // Error
    public string? ErrorMessage => _state.Error?.Message;
    public bool HasError => _state.Error != null;

    // ── Commands ─────────────────────────────────────────────

    public void ToggleAudio() => _session.ToggleAudio();
    public void ToggleVideo() => _session.ToggleVideo();
    public void FlipCamera() => _ = _session.FlipCameraAsync();
    public void Leave() => _session.Leave();
    public void End() => _session.End();
    public void StartScreenShare() => _ = _session.StartScreenShareAsync();
    public void StopScreenShare() => _ = _session.StopScreenShareAsync();

    // ── State change handler ─────────────────────────────────

    private void OnStateChanged(CallState state)
    {
        _state = state;

        // Raise PropertyChanged for all bindable properties
        NotifyAll();
    }

    private void NotifyAll()
    {
        OnPropertyChanged(nameof(Phase));
        OnPropertyChanged(nameof(RoomId));
        OnPropertyChanged(nameof(RoomUrl));
        OnPropertyChanged(nameof(IsJoining));
        OnPropertyChanged(nameof(IsWaiting));
        OnPropertyChanged(nameof(IsInCall));
        OnPropertyChanged(nameof(IsEnding));
        OnPropertyChanged(nameof(IsError));
        OnPropertyChanged(nameof(IsIdle));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(ConnectionStatusText));
        OnPropertyChanged(nameof(IsConnectionDegraded));
        OnPropertyChanged(nameof(LocalCid));
        OnPropertyChanged(nameof(LocalDisplayName));
        OnPropertyChanged(nameof(LocalAudioEnabled));
        OnPropertyChanged(nameof(LocalVideoEnabled));
        OnPropertyChanged(nameof(IsHost));
        OnPropertyChanged(nameof(LocalCameraMode));
        OnPropertyChanged(nameof(AudioButtonIcon));
        OnPropertyChanged(nameof(VideoButtonIcon));
        OnPropertyChanged(nameof(RemoteParticipants));
        OnPropertyChanged(nameof(HasRemoteParticipants));
        OnPropertyChanged(nameof(ParticipantCount));
        OnPropertyChanged(nameof(ErrorMessage));
        OnPropertyChanged(nameof(HasError));
    }

    // ── Cleanup ──────────────────────────────────────────────

    public void Dispose()
    {
        _unsubscribe?.Invoke();
        _unsubscribe = null;
    }

    // ── INotifyPropertyChanged ───────────────────────────────

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
