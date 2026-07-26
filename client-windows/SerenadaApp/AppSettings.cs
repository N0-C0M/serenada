using System.Text.Json;

namespace SerenadaApp;

internal sealed record AppSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public string DisplayName { get; init; } = string.Empty;
    public string ServerHost { get; init; } = HostUtilities.DefaultHost;
    public bool StartWithMicrophone { get; init; } = true;
    public bool StartWithCamera { get; init; } = true;

    public static AppSettings Load()
    {
        try
        {
            var path = SettingsPath();
            if (!File.Exists(path))
                return new AppSettings();

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions)
                ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save()
    {
        var path = SettingsPath();
        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("Settings directory is unavailable.");
        Directory.CreateDirectory(directory);

        var temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(this, JsonOptions));
        File.Move(temporaryPath, path, overwrite: true);
    }

    private static string SettingsPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Serenada",
            "settings.json");
    }
}
