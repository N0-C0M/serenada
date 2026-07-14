namespace Serenada.Core.Models;

/// <summary>
/// Log level for SDK diagnostic output.
/// </summary>
public enum SerenadaLogLevel
{
    /// <summary>Verbose diagnostic information.</summary>
    Debug,

    /// <summary>Informational messages.</summary>
    Info,

    /// <summary>Warning conditions.</summary>
    Warning,

    /// <summary>Error conditions.</summary>
    Error,
}

/// <summary>
/// Logger interface for custom log handling. Implement this to capture SDK logs.
/// </summary>
public interface ISerenadaLogger
{
    /// <summary>
    /// Log a message at the specified level.
    /// </summary>
    /// <param name="level">Severity level.</param>
    /// <param name="tag">Component or subsystem tag.</param>
    /// <param name="message">The log message.</param>
    void Log(SerenadaLogLevel level, string tag, string message);
}
