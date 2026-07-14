using System.Text.Json;
using Serenada.Core.Models;

namespace Serenada.Core;

/// <summary>
/// Persists <see cref="RecoveryRecord"/> for crash recovery across process restarts.
/// Uses local app data storage. Mirrors the web <c>sessionStorage</c> and
/// Android <c>SharedPreferences</c> approaches.
/// </summary>
internal class RecoveryStorage
{
    private static readonly string StorageDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Serenada");

    private static readonly string FilePath = Path.Combine(StorageDir, "recovery.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    /// <summary>
    /// Load a persisted recovery record, if it exists and has not expired.
    /// Returns <c>null</c> if there is no record or it has expired.
    /// </summary>
    public RecoveryRecord? Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return null;

            var json = File.ReadAllText(FilePath);
            var record = JsonSerializer.Deserialize<RecoveryRecord>(json, JsonOptions);
            if (record == null)
                return null;

            // Check expiry
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (record.ExpiresAtMs <= now)
            {
                // Expired — clean up
                Clear();
                return null;
            }

            return record;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Persist a recovery record.
    /// </summary>
    public void Save(RecoveryRecord record)
    {
        try
        {
            Directory.CreateDirectory(StorageDir);
            var json = JsonSerializer.Serialize(record, JsonOptions);
            File.WriteAllText(FilePath, json);
        }
        catch
        {
            // Best-effort — don't crash the SDK on persistence failures.
        }
    }

    /// <summary>
    /// Clear any persisted recovery record.
    /// </summary>
    public void Clear()
    {
        try
        {
            if (File.Exists(FilePath))
                File.Delete(FilePath);
        }
        catch
        {
            // Best-effort.
        }
    }
}
