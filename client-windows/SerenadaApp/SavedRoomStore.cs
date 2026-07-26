using System.Text.Json;

namespace SerenadaApp;

internal sealed record SavedRoom
{
    public string RoomId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public long CreatedAt { get; init; }
    public string Host { get; init; } = HostUtilities.DefaultHost;
    public long? LastJoinedAt { get; init; }
}

internal sealed class SavedRoomStore
{
    private const int MaxSavedRooms = 50;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private static string StoragePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Serenada",
        "saved-rooms.json");

    public IReadOnlyList<SavedRoom> Load()
    {
        try
        {
            if (!File.Exists(StoragePath))
                return [];

            var rooms = JsonSerializer.Deserialize<List<SavedRoom>>(
                    File.ReadAllText(StoragePath),
                    JsonOptions)
                ?? [];
            return Normalize(rooms);
        }
        catch
        {
            return [];
        }
    }

    public void Save(SavedRoom room)
    {
        var normalized = NormalizeRoom(room);
        if (normalized == null)
            throw new ArgumentException("The saved room is invalid.", nameof(room));

        var rooms = Load().ToList();
        var existing = rooms.FirstOrDefault(candidate =>
            candidate.RoomId == normalized.RoomId);
        rooms.RemoveAll(candidate => candidate.RoomId == normalized.RoomId);
        rooms.Insert(0, normalized with
        {
            LastJoinedAt = normalized.LastJoinedAt ?? existing?.LastJoinedAt,
        });
        Persist(rooms.Take(MaxSavedRooms));
    }

    public void Remove(string roomId)
    {
        var rooms = Load()
            .Where(room => room.RoomId != roomId)
            .ToList();
        Persist(rooms);
    }

    public void MarkJoined(string roomId)
    {
        try
        {
            var rooms = Load().ToList();
            var index = rooms.FindIndex(room => room.RoomId == roomId);
            if (index < 0)
                return;
            rooms[index] = rooms[index] with
            {
                LastJoinedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            };
            Persist(rooms);
        }
        catch
        {
            // Room history must never prevent a call from starting.
        }
    }

    private static IReadOnlyList<SavedRoom> Normalize(
        IEnumerable<SavedRoom> rooms)
    {
        var result = new List<SavedRoom>();
        var roomIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var room in rooms)
        {
            var normalized = NormalizeRoom(room);
            if (normalized == null || !roomIds.Add(normalized.RoomId))
                continue;
            result.Add(normalized);
            if (result.Count == MaxSavedRooms)
                break;
        }
        return result.AsReadOnly();
    }

    private static SavedRoom? NormalizeRoom(SavedRoom room)
    {
        var name = HostUtilities.NormalizeRoomName(room.Name);
        var host = HostUtilities.NormalizeHost(room.Host);
        if (!HostUtilities.IsValidRoomId(room.RoomId) ||
            name == null ||
            host == null)
        {
            return null;
        }

        return room with
        {
            Name = name,
            Host = host,
            CreatedAt = Math.Max(room.CreatedAt, 1),
            LastJoinedAt = room.LastJoinedAt is > 0
                ? room.LastJoinedAt
                : null,
        };
    }

    private static void Persist(IEnumerable<SavedRoom> rooms)
    {
        var path = StoragePath;
        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException(
                "Saved-room directory is unavailable.");
        Directory.CreateDirectory(directory);
        var temporaryPath = path + ".tmp";
        File.WriteAllText(
            temporaryPath,
            JsonSerializer.Serialize(rooms, JsonOptions));
        File.Move(temporaryPath, path, overwrite: true);
    }
}
