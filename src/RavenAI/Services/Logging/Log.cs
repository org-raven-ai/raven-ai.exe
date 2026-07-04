namespace RavenAI.Services.Logging;

/// <summary>
/// Process-wide entry point to the unified <see cref="ILogger"/>. Because the app wires services
/// by hand (no DI container), this static façade lets any code — global exception handlers, catch
/// blocks in view models and background callbacks — record to the same logger without threading a
/// logger reference through every constructor. <see cref="Init"/> is called once at startup.
/// Calls made before <see cref="Init"/> (or if it were never called) are safely no-ops.
/// </summary>
public static class Log
{
    private static ILogger? _logger;

    /// <summary>Installs the single logger instance for the process. Call once at startup.</summary>
    public static void Init(ILogger logger) => _logger = logger;

    public static void Debug(string message, string category = "App")
        => _logger?.Log(LogLevel.Debug, message, null, category);

    public static void Info(string message, string category = "App")
        => _logger?.Log(LogLevel.Info, message, null, category);

    public static void Warning(string message, Exception? exception = null, string category = "App")
        => _logger?.Log(LogLevel.Warning, message, exception, category);

    public static void Error(string message, Exception? exception = null, string category = "App")
        => _logger?.Log(LogLevel.Error, message, exception, category);
}
