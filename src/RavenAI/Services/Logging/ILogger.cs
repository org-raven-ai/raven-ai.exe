namespace RavenAI.Services.Logging;

/// <summary>
/// The unified sink for everything worth recording: errors, exceptions, and notable events.
/// A single implementation both appends to a log file and raises <see cref="EntryLogged"/> for
/// the in-app log panel, so nothing is logged to one place without the other.
/// </summary>
public interface ILogger
{
    /// <summary>Raised (on the calling thread) after an entry has been recorded.</summary>
    event Action<LogEntry>? EntryLogged;

    /// <summary>A copy of the most recent in-memory entries, oldest first.</summary>
    IReadOnlyList<LogEntry> Snapshot();

    /// <summary>Records one entry. Never throws — logging must not break the caller.</summary>
    void Log(LogLevel level, string message, Exception? exception = null, string category = "App");
}

/// <summary>Ergonomic level-specific helpers over <see cref="ILogger.Log"/>.</summary>
public static class LoggerExtensions
{
    public static void Debug(this ILogger logger, string message, string category = "App")
        => logger.Log(LogLevel.Debug, message, null, category);

    public static void Info(this ILogger logger, string message, string category = "App")
        => logger.Log(LogLevel.Info, message, null, category);

    public static void Warning(this ILogger logger, string message, Exception? exception = null, string category = "App")
        => logger.Log(LogLevel.Warning, message, exception, category);

    public static void Error(this ILogger logger, string message, Exception? exception = null, string category = "App")
        => logger.Log(LogLevel.Error, message, exception, category);
}
