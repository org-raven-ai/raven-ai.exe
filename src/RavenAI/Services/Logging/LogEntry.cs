namespace RavenAI.Services.Logging;

/// <summary>
/// One immutable log record. Rendered both to the on-disk log file (see <see cref="ToFileLine"/>)
/// and to the in-app log panel (via the display helpers), so the two views never drift.
/// </summary>
/// <param name="Timestamp">When the entry was created.</param>
/// <param name="Level">Severity.</param>
/// <param name="Category">Short source tag (e.g. "Chat", "Speech", "Unhandled").</param>
/// <param name="Message">Human-readable summary.</param>
/// <param name="Detail">Optional extra detail — typically an exception's full ToString().</param>
public sealed record LogEntry(
    DateTimeOffset Timestamp,
    LogLevel Level,
    string Category,
    string Message,
    string? Detail)
{
    /// <summary>Local wall-clock time, for the log panel's leading column.</summary>
    public string TimeText => Timestamp.ToLocalTime().ToString("HH:mm:ss");

    /// <summary>Upper-cased level tag (e.g. "ERROR"), for the panel and file line.</summary>
    public string LevelText => Level.ToString().ToUpperInvariant();

    /// <summary>True when this entry carries exception/stack detail worth showing.</summary>
    public bool HasDetail => !string.IsNullOrEmpty(Detail);

    /// <summary>Single-line (plus optional indented detail) rendering for the log file.</summary>
    public string ToFileLine()
    {
        string head = $"{Timestamp.ToLocalTime():yyyy-MM-dd HH:mm:ss.fff} [{LevelText,-7}] {Category}: {Message}";
        return HasDetail ? head + Environment.NewLine + Detail : head;
    }
}
