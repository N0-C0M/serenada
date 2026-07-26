using System.ComponentModel;
using System.Runtime.CompilerServices;
using Serenada.Core;
using Serenada.Core.Models;

namespace Serenada.CallUI;

/// <summary>
/// Observable bridge from the headless session state to WinUI.
/// </summary>
public sealed class CallViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly SerenadaSession _session;
    private Action? _unsubscribe;
    private CallState _state = new();

    public CallViewModel(SerenadaSession session)
    {
        _session = session;
        _unsubscribe = session.Subscribe(OnStateChanged);
    }

    internal SerenadaSession Session => _session;

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
        CallPhase.Idle => string.Empty,
        CallPhase.AwaitingPermissions => "Waiting for permissions...",
        CallPhase.Joining => "Joining call...",
        CallPhase.Waiting => "Waiting for someone to join...",
        CallPhase.InCall => "In call",
        CallPhase.Ending => "Call ended",
        CallPhase.Error => _state.Error?.Message ?? "An error occurred",
        _ => string.Empty,
    };

    public string ConnectionStatusText => _state.ConnectionStatus switch
    {
        ConnectionStatus.Connected => string.Empty,
        ConnectionStatus.Recovering => "Reconnecting...",
        ConnectionStatus.Retrying => "Connection lost. Retrying...",
        ConnectionStatus.Disconnected => "Disconnected",
        _ => string.Empty,
    };

    public bool IsConnectionDegraded =>
        _state.ConnectionStatus is ConnectionStatus.Recovering or ConnectionStatus.Retrying;

    public string? LocalCid => _state.LocalParticipant?.Cid;
    public string? LocalDisplayName => _state.LocalParticipant?.DisplayName;
    public bool LocalAudioEnabled => _state.LocalParticipant?.AudioEnabled ?? false;
    public bool LocalVideoEnabled => _state.LocalParticipant?.VideoEnabled ?? false;
    public bool IsHost => _state.LocalParticipant?.IsHost ?? false;
    public CameraMode LocalCameraMode =>
        _state.LocalParticipant?.CameraMode ?? CameraMode.Selfie;

    public IReadOnlyList<RemoteParticipant> RemoteParticipants =>
        _state.RemoteParticipants;
    public bool HasRemoteParticipants => _state.RemoteParticipants.Count > 0;
    public int ParticipantCount => _state.ParticipantCount;
    public string? ErrorMessage => _state.Error?.Message;
    public bool HasError => _state.Error != null;

    public void ToggleAudio() => _session.ToggleAudio();
    public void ToggleVideo() => _session.ToggleVideo();
    public void FlipCamera() => _ = _session.FlipCameraAsync();
    public void Leave() => _session.Leave();
    public void End() => _session.End();
    public void StartScreenShare() => _ = _session.StartScreenShareAsync();
    public void StopScreenShare() => _ = _session.StopScreenShareAsync();

    public void Dispose()
    {
        _unsubscribe?.Invoke();
        _unsubscribe = null;
    }

    private void OnStateChanged(CallState state)
    {
        _state = state;
        OnPropertyChanged(string.Empty);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
