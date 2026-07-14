using System.Text.Json;
using System.Text.Json.Serialization;

namespace Serenada.Core.Signaling;

/// <summary>
/// JSON envelope for all signaling messages (WebSocket and SSE).
/// Mirrors the cross-platform v1 protocol spec: <c>{v, type, rid, sid, cid, to, ts, payload}</c>.
/// </summary>
public sealed record SignalingMessage
{
    /// <summary>Protocol version. Always <c>1</c>.</summary>
    [JsonPropertyName("v")]
    public int V { get; init; } = 1;

    /// <summary>Message type (e.g. "join", "offer", "ice", "room_state").</summary>
    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    /// <summary>Room identifier.</summary>
    [JsonPropertyName("rid")]
    public string? Rid { get; init; }

    /// <summary>Session identifier (server-issued for WS; client-or-server for SSE).</summary>
    [JsonPropertyName("sid")]
    public string? Sid { get; init; }

    /// <summary>Client identifier.</summary>
    [JsonPropertyName("cid")]
    public string? Cid { get; init; }

    /// <summary>Target client ID for directed relay messages.</summary>
    [JsonPropertyName("to")]
    public string? To { get; init; }

    /// <summary>Client timestamp in ms since epoch (optional).</summary>
    [JsonPropertyName("ts")]
    public long? Ts { get; init; }

    /// <summary>Message-specific payload.</summary>
    [JsonPropertyName("payload")]
    public JsonElement? Payload { get; init; }

    /// <summary>
    /// Serialize to JSON. Uses the same field ordering as the protocol spec.
    /// </summary>
    public string ToJson()
    {
        return JsonSerializer.Serialize(this, JsonOptions);
    }

    /// <summary>
    /// Deserialize from JSON.
    /// </summary>
    public static SignalingMessage? FromJson(string json)
    {
        return JsonSerializer.Deserialize<SignalingMessage>(json, JsonOptions);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// Fast outbound constructor for messages without a payload.
    /// </summary>
    public static SignalingMessage Outbound(string type, string? rid = null, string? sid = null,
        string? cid = null, string? to = null, object? payload = null)
    {
        JsonElement? payloadElement = null;
        if (payload is not null)
        {
            var json = JsonSerializer.Serialize(payload);
            payloadElement = JsonDocument.Parse(json).RootElement;
        }

        return new SignalingMessage
        {
            Type = type,
            Rid = rid,
            Sid = sid,
            Cid = cid,
            To = to,
            Payload = payloadElement,
        };
    }
}
