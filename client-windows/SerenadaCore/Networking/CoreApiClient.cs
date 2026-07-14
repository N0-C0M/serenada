using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Serenada.Core.Networking;

/// <summary>
/// HTTP API client for Serenada server endpoints.
/// </summary>
internal class CoreApiClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public CoreApiClient()
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
    }

    public CoreApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    // ── Server URL helpers ────────────────────────────────────

    private static string HttpsBaseUrl(string serverHost)
    {
        var scheme = IsLocalHost(serverHost) ? "http" : "https";
        return $"{scheme}://{serverHost}";
    }

    internal static string WsUrl(string serverHost)
    {
        var scheme = IsLocalHost(serverHost) ? "ws" : "wss";
        return $"{scheme}://{serverHost}/ws";
    }

    internal static string SseUrl(string serverHost)
    {
        return $"{HttpsBaseUrl(serverHost)}/sse";
    }

    internal static string BuildRoomUrl(string serverHost, string roomId)
    {
        return $"{HttpsBaseUrl(serverHost)}/call/{roomId}";
    }

    internal static bool IsLocalHost(string host)
    {
        var normalized = host.Trim().ToLowerInvariant();
        return normalized.StartsWith("localhost") ||
               normalized.StartsWith("127.") ||
               normalized.StartsWith("10.0.2.2");
    }

    // ── Endpoints ─────────────────────────────────────────────

    /// <summary>
    /// Create a new room. <c>POST /api/room-id</c>.
    /// </summary>
    public async Task<string> CreateRoomIdAsync(string serverHost, CancellationToken ct = default)
    {
        var url = $"{HttpsBaseUrl(serverHost)}/api/room-id";
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<RoomIdResponse>(JsonOptions, ct);
        if (result?.RoomId is not { Length: > 0 })
            throw new InvalidOperationException("Server returned empty roomId.");
        return result.RoomId;
    }

    /// <summary>
    /// Validate server host reachability. <c>GET /api/room-id</c>.
    /// </summary>
    public async Task<bool> ValidateServerHostAsync(string serverHost, CancellationToken ct = default)
    {
        try
        {
            var url = $"{HttpsBaseUrl(serverHost)}/api/room-id";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            var response = await _httpClient.SendAsync(request, ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Fetch TURN credentials. <c>GET /api/turn-credentials?token=...</c>.
    /// </summary>
    public async Task<TurnCredentialsResponse?> FetchTurnCredentialsAsync(
        string serverHost, string token, CancellationToken ct = default)
    {
        var url = $"{HttpsBaseUrl(serverHost)}/api/turn-credentials?token={Uri.EscapeDataString(token)}";
        var response = await _httpClient.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TurnCredentialsResponse>(JsonOptions, ct);
    }

    /// <summary>
    /// Fetch a diagnostic TURN token. <c>POST /api/diagnostic-token</c>.
    /// </summary>
    public async Task<DiagnosticTokenResponse?> FetchDiagnosticTokenAsync(
        string serverHost, CancellationToken ct = default)
    {
        var url = $"{HttpsBaseUrl(serverHost)}/api/diagnostic-token";
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<DiagnosticTokenResponse>(JsonOptions, ct);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}

// ── Response types ────────────────────────────────────────────

/// <summary>Response from <c>/api/room-id</c>.</summary>
internal sealed record RoomIdResponse
{
    [JsonPropertyName("roomId")]
    public string? RoomId { get; init; }
}

/// <summary>Response from <c>/api/turn-credentials</c>.</summary>
internal sealed record TurnCredentialsResponse
{
    [JsonPropertyName("username")]
    public string? Username { get; init; }

    [JsonPropertyName("password")]
    public string? Password { get; init; }

    [JsonPropertyName("uris")]
    public List<string>? Uris { get; init; }

    [JsonPropertyName("ttl")]
    public int Ttl { get; init; }
}

/// <summary>Response from <c>/api/diagnostic-token</c>.</summary>
internal sealed record DiagnosticTokenResponse
{
    [JsonPropertyName("token")]
    public string? Token { get; init; }

    [JsonPropertyName("expires")]
    public long Expires { get; init; }
}
