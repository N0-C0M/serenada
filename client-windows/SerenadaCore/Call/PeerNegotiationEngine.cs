using System.Text.Json;
using Serenada.Core.Models;
using Serenada.Core.Signaling;
using Serenada.Core.WebRtc;

namespace Serenada.Core.Call;

/// <summary>
/// Owns the per-participant peer slots and deterministic offer/answer flow.
/// All state transitions are marshalled back to the session context.
/// </summary>
internal sealed class PeerNegotiationEngine : IPeerConnectionSlotCallbacks, IDisposable
{
    private const string LegacyOfferId = "__legacy__";

    private readonly ISessionMediaEngine _mediaEngine;
    private readonly Func<string?> _getLocalCid;
    private readonly Func<RoomStatePayload?> _getRoomState;
    private readonly Func<bool> _isSignalingConnected;
    private readonly Func<bool> _deferInitialAnswer;
    private readonly Action<string, string, object?> _sendToPeer;
    private readonly Action<string, IRtcVideoTrack> _onRemoteVideoTrackAdded;
    private readonly Action<string, IRtcVideoTrack> _onRemoteVideoTrackRemoved;
    private readonly Action<string, string> _onPeerConnectionChanged;
    private readonly Action<Action> _dispatch;
    private readonly ISerenadaLogger? _logger;

    private readonly Dictionary<string, IPeerConnectionSlot> _slots = [];
    private readonly Dictionary<string, SemaphoreSlim> _peerLocks = [];
    private readonly Dictionary<string, string> _pendingLocalOffers = [];
    private readonly Dictionary<string, CancellationTokenSource> _offerTimeouts = [];
    private readonly Dictionary<string, CancellationTokenSource> _recoveryTimers = [];
    private readonly Dictionary<string, Dictionary<string, List<RtcIceCandidate>>> _pendingIce = [];
    private readonly Dictionary<string, string> _ignoredOfferIds = [];
    private readonly Dictionary<string, long> _lastMediaRestartHandledAt = [];
    private readonly HashSet<string> _sentInitialOffers = [];
    private readonly HashSet<string> _initialAnswersReceived = [];

    private long _offerSequence;
    private bool _disposed;

    public PeerNegotiationEngine(
        ISessionMediaEngine mediaEngine,
        Func<string?> getLocalCid,
        Func<RoomStatePayload?> getRoomState,
        Func<bool> isSignalingConnected,
        Func<bool> deferInitialAnswer,
        Action<string, string, object?> sendToPeer,
        Action<string, IRtcVideoTrack> onRemoteVideoTrackAdded,
        Action<string, IRtcVideoTrack> onRemoteVideoTrackRemoved,
        Action<string, string> onPeerConnectionChanged,
        Action<Action> dispatch,
        ISerenadaLogger? logger)
    {
        _mediaEngine = mediaEngine;
        _getLocalCid = getLocalCid;
        _getRoomState = getRoomState;
        _isSignalingConnected = isSignalingConnected;
        _deferInitialAnswer = deferInitialAnswer;
        _sendToPeer = sendToPeer;
        _onRemoteVideoTrackAdded = onRemoteVideoTrackAdded;
        _onRemoteVideoTrackRemoved = onRemoteVideoTrackRemoved;
        _onPeerConnectionChanged = onPeerConnectionChanged;
        _dispatch = dispatch;
        _logger = logger;
    }

    public void SyncPeers(RoomStatePayload roomState)
    {
        if (_disposed) return;

        var localCid = _getLocalCid();
        if (string.IsNullOrWhiteSpace(localCid)) return;

        var remoteParticipants = roomState.Participants
            .Where(p => p.Cid != localCid)
            .ToDictionary(p => p.Cid, StringComparer.Ordinal);

        foreach (var departedCid in _slots.Keys
                     .Where(cid => !remoteParticipants.ContainsKey(cid))
                     .ToList())
        {
            RemoveSlot(departedCid);
        }

        foreach (var participant in remoteParticipants.Values)
        {
            if (participant.ConnectionStatus ==
                SignalingProtocolConstants.ConnectionStatusSuspended)
            {
                continue;
            }

            var supportsIndependentContent =
                _mediaEngine.SupportsIndependentContentVideo &&
                participant.Capabilities?.IndependentContentVideo == true;
            if (_slots.TryGetValue(participant.Cid, out var existing) &&
                existing.SupportsIndependentContentVideo != supportsIndependentContent)
            {
                RemoveSlot(participant.Cid, clearInitialAnswer: false);
            }

            var slot = GetOrCreateSlot(participant.Cid);
            if (ShouldOffer(participant.Cid, roomState))
                StartOffer(participant.Cid, slot);
        }
    }

    public void ProcessSignalingPayload(PeerMessage message)
    {
        if (_disposed) return;

        var fromCid = ResolveFromCid(message);
        if (string.IsNullOrWhiteSpace(fromCid) || fromCid == _getLocalCid())
            return;

        var roomState = _getRoomState();
        if (roomState != null && roomState.Participants.All(p => p.Cid != fromCid))
        {
            Log(SerenadaLogLevel.Debug,
                $"Ignoring {message.Type} from departed peer {fromCid}.");
            return;
        }

        RunPeerOperation(fromCid, async () =>
        {
            var slot = GetOrCreateSlot(fromCid);
            switch (message.Type)
            {
                case SignalingProtocolConstants.TypeOffer:
                    await HandleOfferAsync(fromCid, slot, message.Payload);
                    break;
                case SignalingProtocolConstants.TypeAnswer:
                    await HandleAnswerAsync(fromCid, slot, message.Payload);
                    break;
                case SignalingProtocolConstants.TypeIce:
                    await HandleIceAsync(fromCid, slot, message.Payload);
                    break;
                case SignalingProtocolConstants.TypeMediaRestartRequest:
                    HandleMediaRestartRequest(fromCid, slot, message.Payload);
                    break;
            }
        });
    }

    public void ScheduleDirtyPairRestart(string remoteCid)
    {
        if (!_slots.ContainsKey(remoteCid)) return;
        RecreateSlot(remoteCid, offerIfOwner: true);
    }

    public void SendSignaling(string type, string to, object? payload)
    {
        _dispatch(() =>
        {
            if (!_disposed && _slots.ContainsKey(to))
                _sendToPeer(to, type, payload);
        });
    }

    public void OnRemoteVideoTrackAdded(string cid, IRtcVideoTrack track)
    {
        _dispatch(() =>
        {
            if (!_disposed && _slots.ContainsKey(cid))
                _onRemoteVideoTrackAdded(cid, track);
        });
    }

    public void OnRemoteVideoTrackRemoved(string cid, IRtcVideoTrack track)
    {
        _dispatch(() =>
        {
            if (!_disposed)
                _onRemoteVideoTrackRemoved(cid, track);
        });
    }

    public void OnRemoteAudioTrackAdded(string cid, IRtcAudioTrack track)
    {
        // MR-WebRTC renders remote audio through the native audio pipeline.
    }

    public void OnIceConnectionChanged(string cid, string state)
    {
        _dispatch(() =>
        {
            if (_disposed || !_slots.ContainsKey(cid)) return;
            _onPeerConnectionChanged(cid, state);
            if (state is "connected" or "completed")
                CancelPeerRecovery(cid);
            if (state is "failed" or "disconnected")
                SchedulePeerRecovery(
                    cid,
                    $"ICE {state}",
                    state == "disconnected" ? 2_000 : 0);
        });
    }

    public void OnConnectionChanged(string cid, string state)
    {
        _dispatch(() =>
        {
            if (_disposed || !_slots.ContainsKey(cid)) return;
            _onPeerConnectionChanged(cid, state);
            if (state == "connected")
                CancelPeerRecovery(cid);
            if (state is "failed" or "disconnected")
                SchedulePeerRecovery(
                    cid,
                    $"peer connection {state}",
                    state == "disconnected" ? 2_000 : 0);
        });
    }

    public void OnRenegotiationNeeded(string cid)
    {
        _dispatch(() =>
        {
            if (_disposed || !_slots.TryGetValue(cid, out var slot)) return;
            if (ShouldOffer(cid))
                StartOffer(cid, slot, force: true);
            else
                RequestOwnerNegotiation(cid);
        });
    }

    public void OnOutboundMediaStalled(string cid)
    {
        _dispatch(() => RecoverPeer(cid, "outbound media stalled"));
    }

    public void OnInboundLivenessChanged(
        string cid,
        bool cameraReceiving,
        bool contentReceiving)
    {
        // Liveness sampling is added independently from negotiation.
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var timeout in _offerTimeouts.Values)
        {
            timeout.Cancel();
            timeout.Dispose();
        }
        _offerTimeouts.Clear();
        foreach (var timer in _recoveryTimers.Values)
        {
            timer.Cancel();
            timer.Dispose();
        }
        _recoveryTimers.Clear();

        foreach (var cid in _slots.Keys.ToList())
            RemoveSlot(cid);

        foreach (var peerLock in _peerLocks.Values)
            peerLock.Dispose();
        _peerLocks.Clear();
        _pendingIce.Clear();
    }

    private IPeerConnectionSlot GetOrCreateSlot(string remoteCid)
    {
        if (_slots.TryGetValue(remoteCid, out var existing))
            return existing;

        var signalingParticipant = _getRoomState()?.Participants
            .FirstOrDefault(p => p.Cid == remoteCid);
        var participant = signalingParticipant == null
            ? new RemoteParticipant { Cid = remoteCid }
            : MapParticipant(signalingParticipant);

        var slot = _mediaEngine.CreateSlot(participant, this);
        _slots[remoteCid] = slot;
        return slot;
    }

    private void RemoveSlot(string remoteCid, bool clearInitialAnswer = true)
    {
        CancelOfferTimeout(remoteCid);
        CancelPeerRecovery(remoteCid);
        _pendingLocalOffers.Remove(remoteCid);
        _pendingIce.Remove(remoteCid);
        _ignoredOfferIds.Remove(remoteCid);
        _lastMediaRestartHandledAt.Remove(remoteCid);
        _sentInitialOffers.Remove(remoteCid);
        if (clearInitialAnswer)
            _initialAnswersReceived.Remove(remoteCid);

        if (_slots.Remove(remoteCid, out var slot))
            _mediaEngine.RemoveSlot(slot);
    }

    private void RecreateSlot(string remoteCid, bool offerIfOwner)
    {
        if (_disposed) return;

        RemoveSlot(remoteCid, clearInitialAnswer: false);
        var roomState = _getRoomState();
        if (roomState == null ||
            roomState.Participants.All(p => p.Cid != remoteCid))
        {
            return;
        }

        var slot = GetOrCreateSlot(remoteCid);
        if (offerIfOwner && ShouldOffer(remoteCid, roomState))
            StartOffer(remoteCid, slot, force: true);
    }

    private void StartOffer(
        string remoteCid,
        IPeerConnectionSlot slot,
        bool force = false)
    {
        if (_disposed ||
            !_isSignalingConnected() ||
            !IsParticipantActive(remoteCid) ||
            !ShouldOffer(remoteCid) ||
            slot.SignalingState != "stable")
        {
            return;
        }

        if (_pendingLocalOffers.ContainsKey(remoteCid))
            return;
        if (!force && _sentInitialOffers.Contains(remoteCid))
            return;

        RunPeerOperation(remoteCid, async () =>
        {
            if (_pendingLocalOffers.ContainsKey(remoteCid) ||
                !_slots.TryGetValue(remoteCid, out var currentSlot) ||
                !ReferenceEquals(slot, currentSlot) ||
                currentSlot.SignalingState != "stable")
            {
                return;
            }

            var offerId = NextOfferId(remoteCid);
            _pendingLocalOffers[remoteCid] = offerId;
            slot.SetNegotiationId(offerId);

            try
            {
                var offer = await slot.CreateOfferAsync();
                if (_pendingLocalOffers.GetValueOrDefault(remoteCid) != offerId)
                    return;

                _sendToPeer(remoteCid, SignalingProtocolConstants.TypeOffer, new
                {
                    sdp = offer.Sdp,
                    offerId,
                });
                _sentInitialOffers.Add(remoteCid);
                ScheduleOfferTimeout(remoteCid, offerId);
            }
            catch
            {
                _pendingLocalOffers.Remove(remoteCid);
                throw;
            }
        });
    }

    private async Task HandleOfferAsync(
        string remoteCid,
        IPeerConnectionSlot slot,
        JsonElement? payload)
    {
        if (!TryGetString(payload, "sdp", out var sdp))
            return;

        var offerId = GetOfferId(payload);
        var offerCollision = slot.SignalingState != "stable";
        if (offerCollision && ShouldOffer(remoteCid))
        {
            _ignoredOfferIds[remoteCid] = offerId;
            Log(SerenadaLogLevel.Debug,
                $"Ignoring colliding offer {offerId} from {remoteCid}.");
            StartOffer(remoteCid, slot);
            return;
        }

        _ignoredOfferIds.Remove(remoteCid);
        CancelOfferTimeout(remoteCid);
        _pendingLocalOffers.Remove(remoteCid);
        slot.SetNegotiationId(offerId);
        await slot.SetRemoteDescriptionAsync(new RtcSessionDescription
        {
            Type = RtcSdpType.Offer,
            Sdp = sdp,
        });
        await FlushPendingIceAsync(remoteCid, offerId, slot);

        var answer = await slot.CreateAnswerAsync();
        _sendToPeer(remoteCid, SignalingProtocolConstants.TypeAnswer, new
        {
            sdp = answer.Sdp,
            offerId,
        });
    }

    private async Task HandleAnswerAsync(
        string remoteCid,
        IPeerConnectionSlot slot,
        JsonElement? payload)
    {
        if (!TryGetString(payload, "sdp", out var sdp))
            return;

        var offerId = GetOfferId(payload);
        var pendingOfferId = _pendingLocalOffers.GetValueOrDefault(remoteCid);
        if (slot.SignalingState != "havelocaloffer")
        {
            Log(SerenadaLogLevel.Debug,
                $"Dropping stale answer {offerId} from {remoteCid} in {slot.SignalingState}.");
            return;
        }
        if (offerId != LegacyOfferId && pendingOfferId != offerId)
        {
            Log(SerenadaLogLevel.Debug,
                $"Dropping stale answer {offerId} from {remoteCid}.");
            return;
        }

        var completedOfferId = pendingOfferId ?? offerId;
        slot.SetNegotiationId(completedOfferId);
        await slot.SetRemoteDescriptionAsync(new RtcSessionDescription
        {
            Type = RtcSdpType.Answer,
            Sdp = sdp,
        });

        CancelOfferTimeout(remoteCid);
        _pendingLocalOffers.Remove(remoteCid);
        _initialAnswersReceived.Add(remoteCid);
        await FlushPendingIceAsync(remoteCid, completedOfferId, slot);
    }

    private async Task HandleIceAsync(
        string remoteCid,
        IPeerConnectionSlot slot,
        JsonElement? payload)
    {
        if (payload is not { } root ||
            !root.TryGetProperty("candidate", out var candidateJson) ||
            candidateJson.ValueKind != JsonValueKind.Object ||
            !TryGetString(candidateJson, "candidate", out var candidateSdp))
        {
            return;
        }

        var candidate = new RtcIceCandidate
        {
            Candidate = candidateSdp,
            SdpMid = TryGetString(candidateJson, "sdpMid", out var sdpMid)
                ? sdpMid
                : string.Empty,
            SdpMLineIndex = TryGetInt(candidateJson, "sdpMLineIndex", out var index)
                ? index
                : 0,
        };
        var offerId = GetOfferId(payload);

        if (_ignoredOfferIds.GetValueOrDefault(remoteCid) == offerId)
            return;

        if (offerId != LegacyOfferId &&
            offerId != slot.CurrentNegotiationId &&
            offerId != _pendingLocalOffers.GetValueOrDefault(remoteCid))
        {
            BufferIce(remoteCid, offerId, candidate);
            return;
        }

        await slot.AddIceCandidateAsync(candidate);
    }

    private void HandleMediaRestartRequest(
        string remoteCid,
        IPeerConnectionSlot slot,
        JsonElement? payload)
    {
        if (!ShouldOffer(remoteCid))
            return;

        var reason = TryGetString(payload, "reason", out var parsedReason)
            ? parsedReason
            : string.Empty;
        if (reason == SignalingProtocolConstants.MediaRestartLocalTrackNegotiation)
        {
            StartOffer(remoteCid, slot, force: true);
            return;
        }

        if (_deferInitialAnswer() &&
            !_initialAnswersReceived.Contains(remoteCid))
        {
            Log(SerenadaLogLevel.Debug,
                $"Ignoring media restart from {remoteCid} before the deferred initial answer.");
            return;
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (_lastMediaRestartHandledAt.TryGetValue(remoteCid, out var last) &&
            now - last < WebRtcResilienceConstants.OutboundMediaRecoveryCooldownMs)
        {
            return;
        }

        _lastMediaRestartHandledAt[remoteCid] = now;
        RecreateSlot(remoteCid, offerIfOwner: true);
    }

    private void RecoverPeer(string remoteCid, string reason)
    {
        if (!_slots.ContainsKey(remoteCid) || !IsParticipantActive(remoteCid))
            return;

        Log(SerenadaLogLevel.Warning, $"Recovering {remoteCid} after {reason}.");
        if (ShouldOffer(remoteCid))
            RecreateSlot(remoteCid, offerIfOwner: true);
        else
            RequestOwnerNegotiation(remoteCid);
    }

    private void SchedulePeerRecovery(
        string remoteCid,
        string reason,
        int delayMs)
    {
        if (_recoveryTimers.ContainsKey(remoteCid))
            return;

        var cts = new CancellationTokenSource();
        _recoveryTimers[remoteCid] = cts;
        _ = WaitForPeerRecoveryAsync(remoteCid, reason, delayMs, cts.Token);
    }

    private async Task WaitForPeerRecoveryAsync(
        string remoteCid,
        string reason,
        int delayMs,
        CancellationToken ct)
    {
        try
        {
            if (delayMs > 0)
                await Task.Delay(delayMs, ct);
            _dispatch(() =>
            {
                if (_disposed || ct.IsCancellationRequested)
                    return;
                CancelPeerRecovery(remoteCid);
                RecoverPeer(remoteCid, reason);
            });
        }
        catch (OperationCanceledException)
        {
            // Expected when connectivity recovers before the grace period.
        }
    }

    private void CancelPeerRecovery(string remoteCid)
    {
        if (!_recoveryTimers.Remove(remoteCid, out var cts))
            return;
        cts.Cancel();
        cts.Dispose();
    }

    private void RequestOwnerNegotiation(string remoteCid)
    {
        if (!_isSignalingConnected() || !IsParticipantActive(remoteCid))
            return;

        _sendToPeer(remoteCid,
            SignalingProtocolConstants.TypeMediaRestartRequest,
            new { reason = SignalingProtocolConstants.MediaRestartLocalTrackNegotiation });
    }

    private bool ShouldOffer(string remoteCid, RoomStatePayload? roomState = null)
    {
        var localCid = _getLocalCid();
        roomState ??= _getRoomState();
        if (string.IsNullOrWhiteSpace(localCid) || roomState == null)
            return false;

        if (_deferInitialAnswer())
        {
            var cids = roomState.Participants.Select(p => p.Cid).ToHashSet();
            if (cids.Count <= 2 && cids.Contains(roomState.HostCid))
                return localCid == roomState.HostCid;
        }

        return string.CompareOrdinal(localCid, remoteCid) < 0;
    }

    private bool IsParticipantActive(string remoteCid)
    {
        var participant = _getRoomState()?.Participants
            .FirstOrDefault(p => p.Cid == remoteCid);
        return participant != null &&
               participant.ConnectionStatus !=
               SignalingProtocolConstants.ConnectionStatusSuspended;
    }

    private string NextOfferId(string remoteCid)
    {
        _offerSequence++;
        return $"{_getLocalCid()}:{remoteCid}:" +
               $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}:{_offerSequence}";
    }

    private void ScheduleOfferTimeout(string remoteCid, string offerId)
    {
        CancelOfferTimeout(remoteCid);
        if (_deferInitialAnswer() &&
            ShouldOffer(remoteCid) &&
            !_initialAnswersReceived.Contains(remoteCid))
        {
            return;
        }

        var cts = new CancellationTokenSource();
        _offerTimeouts[remoteCid] = cts;
        _ = WaitForOfferTimeoutAsync(remoteCid, offerId, cts.Token);
    }

    private async Task WaitForOfferTimeoutAsync(
        string remoteCid,
        string offerId,
        CancellationToken ct)
    {
        try
        {
            await Task.Delay(WebRtcResilienceConstants.OfferTimeoutMs, ct);
            _dispatch(() =>
            {
                if (_disposed ||
                    _pendingLocalOffers.GetValueOrDefault(remoteCid) != offerId)
                {
                    return;
                }

                Log(SerenadaLogLevel.Warning,
                    $"Offer {offerId} to {remoteCid} timed out.");
                RecreateSlot(remoteCid, offerIfOwner: true);
            });
        }
        catch (OperationCanceledException)
        {
            // Expected when the matching answer arrives.
        }
    }

    private void CancelOfferTimeout(string remoteCid)
    {
        if (!_offerTimeouts.Remove(remoteCid, out var cts))
            return;
        cts.Cancel();
        cts.Dispose();
    }

    private void BufferIce(
        string remoteCid,
        string offerId,
        RtcIceCandidate candidate)
    {
        if (!_pendingIce.TryGetValue(remoteCid, out var byOffer))
        {
            byOffer = [];
            _pendingIce[remoteCid] = byOffer;
        }
        if (!byOffer.TryGetValue(offerId, out var candidates))
        {
            candidates = [];
            byOffer[offerId] = candidates;
        }
        if (candidates.Count < WebRtcResilienceConstants.IceCandidateBufferMax)
            candidates.Add(candidate);
    }

    private async Task FlushPendingIceAsync(
        string remoteCid,
        string offerId,
        IPeerConnectionSlot slot)
    {
        if (!_pendingIce.TryGetValue(remoteCid, out var byOffer) ||
            !byOffer.Remove(offerId, out var candidates))
        {
            return;
        }

        foreach (var candidate in candidates)
            await slot.AddIceCandidateAsync(candidate);
        if (byOffer.Count == 0)
            _pendingIce.Remove(remoteCid);
    }

    private void RunPeerOperation(string remoteCid, Func<Task> operation)
    {
        _ = RunPeerOperationAsync(remoteCid, operation);
    }

    private async Task RunPeerOperationAsync(
        string remoteCid,
        Func<Task> operation)
    {
        if (_disposed) return;

        if (!_peerLocks.TryGetValue(remoteCid, out var peerLock))
        {
            peerLock = new SemaphoreSlim(1, 1);
            _peerLocks[remoteCid] = peerLock;
        }

        await peerLock.WaitAsync();
        try
        {
            if (!_disposed)
                await operation();
        }
        catch (Exception ex)
        {
            Log(SerenadaLogLevel.Error,
                $"Negotiation with {remoteCid} failed: {ex.Message}");
            _dispatch(() =>
            {
                if (!_disposed && _slots.ContainsKey(remoteCid))
                    SchedulePeerRecovery(
                        remoteCid,
                        "negotiation failure",
                        500);
            });
        }
        finally
        {
            peerLock.Release();
        }
    }

    private static string ResolveFromCid(PeerMessage message)
    {
        if (!string.IsNullOrWhiteSpace(message.From))
            return message.From;
        return TryGetString(message.Payload, "from", out var from)
            ? from
            : string.Empty;
    }

    private static string GetOfferId(JsonElement? payload)
    {
        if (TryGetString(payload, "offerId", out var offerId))
            return offerId;
        if (TryGetString(payload, "negotiationId", out var negotiationId))
            return negotiationId;
        return LegacyOfferId;
    }

    private static bool TryGetString(
        JsonElement? element,
        string propertyName,
        out string value)
    {
        if (element is { ValueKind: JsonValueKind.Object } root &&
            root.TryGetProperty(propertyName, out var property) &&
            property.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(property.GetString()))
        {
            value = property.GetString()!;
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static bool TryGetInt(
        JsonElement element,
        string propertyName,
        out int value)
    {
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(propertyName, out var property) &&
            property.TryGetInt32(out value))
        {
            return true;
        }

        value = 0;
        return false;
    }

    private static RemoteParticipant MapParticipant(SignalingParticipant participant)
    {
        return new RemoteParticipant
        {
            Cid = participant.Cid,
            DisplayName = participant.DisplayName,
            PeerId = participant.PeerId,
            AudioEnabled = participant.AudioEnabled,
            VideoEnabled = participant.VideoEnabled,
            CameraEnabled = participant.VideoEnabled,
            SupportsIndependentContentVideo =
                participant.Capabilities?.IndependentContentVideo == true,
        };
    }

    private void Log(SerenadaLogLevel level, string message)
    {
        _logger?.Log(level, "Negotiation", message);
    }
}
