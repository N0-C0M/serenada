using Serenada.Core.Models;

namespace SerenadaApp;

internal sealed class FileSerenadaLogger : ISerenadaLogger
{
    private const long MaxLogBytes = 2 * 1024 * 1024;
    private readonly object _writeLock = new();

    public static FileSerenadaLogger Instance { get; } = new();
    public static string LogDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Serenada");

    private static string LogPath => Path.Combine(LogDirectory, "serenada.log");

    private FileSerenadaLogger() { }

    public void Log(
        SerenadaLogLevel level,
        string tag,
        string message)
    {
        try
        {
            lock (_writeLock)
            {
                Directory.CreateDirectory(LogDirectory);
                RotateIfNeeded();
                File.AppendAllText(
                    LogPath,
                    $"{DateTimeOffset.Now:O} [{level}] [{tag}] {message}" +
                    Environment.NewLine);
            }
        }
        catch
        {
            // Diagnostics must never affect the call path.
        }
    }

    private static void RotateIfNeeded()
    {
        var log = new FileInfo(LogPath);
        if (!log.Exists || log.Length < MaxLogBytes)
            return;

        File.Move(LogPath, Path.Combine(LogDirectory, "serenada.previous.log"),
            overwrite: true);
    }
}
