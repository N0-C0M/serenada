using System.Text.RegularExpressions;

namespace SerenadaApp;

internal sealed record RoomTarget(
    string RoomId,
    string? Host = null,
    string? SavedRoomName = null);

internal static partial class HostUtilities
{
    public const string DefaultHost = "serenada.app";
    public const string RussiaHost = "serenada-app.ru";

    [GeneratedRegex("^[A-Za-z0-9_-]{27}$")]
    private static partial Regex RoomIdPattern();

    public static string? NormalizeHost(string? hostInput)
    {
        var raw = hostInput?.Trim();
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var withScheme = raw.StartsWith("http://",
                StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                ? raw
                : $"https://{raw}";
        if (!Uri.TryCreate(withScheme, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") ||
            !string.IsNullOrWhiteSpace(uri.UserInfo) ||
            !string.IsNullOrWhiteSpace(uri.Query) ||
            !string.IsNullOrWhiteSpace(uri.Fragment) ||
            (uri.AbsolutePath.Length > 0 && uri.AbsolutePath != "/") ||
            string.IsNullOrWhiteSpace(uri.Host))
        {
            return null;
        }

        if (uri.Port <= 0 || uri.Port > 65535)
            return null;

        var host = uri.Host.ToLowerInvariant();
        return uri.IsDefaultPort ? host : $"{host}:{uri.Port}";
    }

    public static RoomTarget? ParseRoomTarget(string input)
    {
        var trimmed = input.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return null;

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) &&
            uri.Scheme is "http" or "https")
        {
            var roomId = uri.AbsolutePath
                .TrimEnd('/')
                .Split('/', StringSplitOptions.RemoveEmptyEntries)
                .LastOrDefault();
            if (!IsValidRoomId(roomId))
                return null;

            var query = ParseQuery(uri.Query);
            query.TryGetValue("host", out var queryHost);
            query.TryGetValue("name", out var roomName);
            var host = NormalizeHost(queryHost) ??
                NormalizeHost(uri.Authority);
            return new RoomTarget(
                roomId!,
                host,
                NormalizeRoomName(roomName));
        }

        var lastSegment = trimmed
            .TrimEnd('/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .LastOrDefault();
        return IsValidRoomId(lastSegment)
            ? new RoomTarget(lastSegment!)
            : null;
    }

    public static string BuildSavedRoomInviteLink(SavedRoom room)
    {
        var host = NormalizeHost(room.Host) ?? DefaultHost;
        var appLinkHost = string.Equals(
            host,
            RussiaHost,
            StringComparison.OrdinalIgnoreCase)
                ? RussiaHost
                : DefaultHost;
        return $"https://{appLinkHost}/call/{room.RoomId}" +
            $"?host={Uri.EscapeDataString(host)}" +
            $"&name={Uri.EscapeDataString(room.Name)}";
    }

    public static string? NormalizeRoomName(string? name)
    {
        var trimmed = name?.Trim();
        return string.IsNullOrWhiteSpace(trimmed)
            ? null
            : trimmed[..Math.Min(trimmed.Length, 120)];
    }

    public static bool IsValidRoomId(string? roomId)
    {
        return roomId != null && RoomIdPattern().IsMatch(roomId);
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var values = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var pair in query.TrimStart('?').Split(
                     '&',
                     StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            var key = separator >= 0 ? pair[..separator] : pair;
            var value = separator >= 0 ? pair[(separator + 1)..] : string.Empty;
            try
            {
                values[Uri.UnescapeDataString(key.Replace('+', ' '))] =
                    Uri.UnescapeDataString(value.Replace('+', ' '));
            }
            catch (UriFormatException)
            {
                // Ignore malformed query parameters and keep parsing the link.
            }
        }
        return values;
    }
}
