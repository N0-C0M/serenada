using System.Text.Json;

namespace Serenada.Core.Signaling;

// ── Participant structures ────────────────────────────────────

/// <summary>
/// A participant as reported by the server in <c>joined</c> and <c>room_state</c> payloads.
/// </summary>
public sealed record SignalingParticipant
{
    public string Cid { get; init; } = string.Empty;
    public long? JoinedAt { get; init; }
    public string? DisplayName { get; init; }
    public string? PeerId { get; init; }
    public bool AudioEnabled { get; init; } = true;
    public bool VideoEnabled { get; init; } = true;
    public string? ConnectionStatus { get; init; }
    public ParticipantContentState? ContentState { get; init; }
    public ParticipantCapabilities? Capabilities { get; init; }
    public ParticipantMediaPolicy? MediaPolicy { get; init; }
}

/// <summary>Ephemeral content (screen share) metadata per participant.</summary>
public sealed record ParticipantContentState
{
    public bool Active { get; init; }
    public string? ContentType { get; init; }
    public long? Revision { get; init; }
}

/// <summary>Allowlisted participant capabilities advertised at join.</summary>
public sealed record ParticipantCapabilities
{
    public bool TrickleIce { get; init; } = true;
    public int MaxParticipants { get; init; } = 2;
    public bool IndependentContentVideo { get; init; }
}

/// <summary>Media policy advertised at join.</summary>
public sealed record ParticipantMediaPolicy
{
    public bool VideoMediaEnabled { get; init; } = true;
}

// ── Payload types ─────────────────────────────────────────────

/// <summary>Parsed <c>joined</c> message payload.</summary>
public sealed record JoinedPayload
{
    public string HostCid { get; init; } = string.Empty;
    public IReadOnlyList<SignalingParticipant> Participants { get; init; } = [];
    public int MaxParticipants { get; init; } = 2;
    public string? TurnToken { get; init; }
    public long? TurnTokenExpiresAt { get; init; }
    public string? ReconnectToken { get; init; }
    public int? ReconnectTokenTtlMs { get; init; }
    public long? Epoch { get; init; }
    public string? Reconnect { get; init; }
}

/// <summary>Parsed <c>room_state</c> message payload.</summary>
public sealed record RoomStatePayload
{
    public string HostCid { get; init; } = string.Empty;
    public IReadOnlyList<SignalingParticipant> Participants { get; init; } = [];
    public int MaxParticipants { get; init; } = 2;
    public long? Epoch { get; init; }
}

/// <summary>Parsed <c>error</c> message payload.</summary>
public sealed record ErrorPayload
{
    public string Code { get; init; } = string.Empty;
    public string? Message { get; init; }
    public string? Reason { get; init; }
}

/// <summary>Parsed <c>content_state</c> peer message payload.</summary>
public sealed record ContentStatePayload
{
    public string? FromCid { get; init; }
    public string? Sid { get; init; }
    public bool Active { get; init; }
    public string? ContentType { get; init; }
    public long? Revision { get; init; }
}

/// <summary>Parsed <c>participant_media_state</c> peer message payload.</summary>
public sealed record MediaStatePayload
{
    public string? FromCid { get; init; }
    public bool AudioEnabled { get; init; } = true;
    public bool VideoEnabled { get; init; } = true;
}

/// <summary>Parsed <c>turn-refreshed</c> message payload.</summary>
public sealed record TurnRefreshedPayload
{
    public string TurnToken { get; init; } = string.Empty;
    public int? TurnTokenTtlMs { get; init; }
}

/// <summary>Parsed <c>reconnect-token-refreshed</c> message payload.</summary>
public sealed record ReconnectTokenRefreshedPayload
{
    public string ReconnectToken { get; init; } = string.Empty;
    public int? ReconnectTokenTtlMs { get; init; }
}

/// <summary>Parsed relayed SDP offer.</summary>
public sealed record OfferPayload
{
    public string From { get; init; } = string.Empty;
    public string? OfferId { get; init; }
    public string Sdp { get; init; } = string.Empty;
}

/// <summary>Parsed relayed SDP answer.</summary>
public sealed record AnswerPayload
{
    public string From { get; init; } = string.Empty;
    public string? OfferId { get; init; }
    public string Sdp { get; init; } = string.Empty;
}

/// <summary>Parsed relayed ICE candidate.</summary>
public sealed record IceCandidatePayload
{
    public string From { get; init; } = string.Empty;
    public string? OfferId { get; init; }
    public IceCandidateData? Candidate { get; init; }
}

/// <summary>ICE candidate data as transmitted on the wire.</summary>
public sealed record IceCandidateData
{
    public string Candidate { get; init; } = string.Empty;
    public string? SdpMid { get; init; }
    public int? SdpMLineIndex { get; init; }
    public string? UsernameFragment { get; init; }
}

/// <summary>Parsed <c>negotiation_dirty</c> message payload.</summary>
public sealed record NegotiationDirtyPayload
{
    public string With { get; init; } = string.Empty;
}

/// <summary>Parsed <c>relay_failed</c> message payload.</summary>
public sealed record RelayFailedPayload
{
    public string Reason { get; init; } = string.Empty;
    public IReadOnlyList<string> Targets { get; init; } = [];
    public string? Of { get; init; }
}

// ── Parser functions (mirror payloads.ts / SignalingPayloads.kt) ─

/// <summary>
/// Safe JSON payload parsers. All return <c>null</c> for malformed input.
/// </summary>
public static class SignalingPayloadParsers
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static JoinedPayload? ParseJoined(JsonElement? payload)
    {
        if (payload is not { } p) return null;
        try { return JsonSerializer.Deserialize<JoinedPayload>(p.GetRawText(), JsonOptions); }
        catch { return null; }
    }

    public static RoomStatePayload? ParseRoomState(JsonElement? payload)
    {
        if (payload is not { } p) return null;
        try { return JsonSerializer.Deserialize<RoomStatePayload>(p.GetRawText(), JsonOptions); }
        catch { return null; }
    }

    public static ErrorPayload? ParseError(JsonElement? payload)
    {
        if (payload is not { } p) return null;
        try { return JsonSerializer.Deserialize<ErrorPayload>(p.GetRawText(), JsonOptions); }
        catch { return null; }
    }

    public static ContentStatePayload? ParseContentState(JsonElement? payload, string? sid)
    {
        if (payload is not { } p) return null;
        try
        {
            var result = JsonSerializer.Deserialize<ContentStatePayload>(p.GetRawText(), JsonOptions);
            if (result is not null && sid is not null)
                return result with { Sid = sid };
            return result;
        }
        catch { return null; }
    }

    public static MediaStatePayload? ParseMediaState(JsonElement? payload)
    {
        if (payload is not { } p) return null;
        try { return JsonSerializer.Deserialize<MediaStatePayload>(p.GetRawText(), JsonOptions); }
        catch { return null; }
    }

    public static TurnRefreshedPayload? ParseTurnRefreshed(JsonElement? payload)
    {
        if (payload is not { } p) return null;
        try { return JsonSerializer.Deserialize<TurnRefreshedPayload>(p.GetRawText(), JsonOptions); }
        catch { return null; }
    }

    public static ReconnectTokenRefreshedPayload? ParseReconnectTokenRefreshed(JsonElement? payload)
    {
        if (payload is not { } p) return null;
        try { return JsonSerializer.Deserialize<ReconnectTokenRefreshedPayload>(p.GetRawText(), JsonOptions); }
        catch { return null; }
    }

    public static OfferPayload? ParseOffer(JsonElement? payload)
    {
        if (payload is not { } p) return null;
        try { return JsonSerializer.Deserialize<OfferPayload>(p.GetRawText(), JsonOptions); }
        catch { return null; }
    }

    public static AnswerPayload? ParseAnswer(JsonElement? payload)
    {
        if (payload is not { } p) return null;
        try { return JsonSerializer.Deserialize<AnswerPayload>(p.GetRawText(), JsonOptions); }
        catch { return null; }
    }

    public static IceCandidatePayload? ParseIce(JsonElement? payload)
    {
        if (payload is not { } p) return null;
        try { return JsonSerializer.Deserialize<IceCandidatePayload>(p.GetRawText(), JsonOptions); }
        catch { return null; }
    }

    public static NegotiationDirtyPayload? ParseNegotiationDirty(JsonElement? payload)
    {
        if (payload is not { } p) return null;
        try { return JsonSerializer.Deserialize<NegotiationDirtyPayload>(p.GetRawText(), JsonOptions); }
        catch { return null; }
    }

    public static RelayFailedPayload? ParseRelayFailed(JsonElement? payload)
    {
        if (payload is not { } p) return null;
        try { return JsonSerializer.Deserialize<RelayFailedPayload>(p.GetRawText(), JsonOptions); }
        catch { return null; }
    }
}
