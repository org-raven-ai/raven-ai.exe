using System.IO;
using System.Text;

namespace RavenAI.Services.Logging;

/// <summary>
/// The unified logger. Every entry is, atomically under one lock:
///   1. appended to a daily log file under %APPDATA%\raven_ai\logs, and
///   2. kept in a bounded in-memory ring buffer, then
///   3. broadcast via <see cref="EntryLogged"/> so the in-app log panel can show it live.
///
/// Thread-safe: services raise errors from background threads (audio pumps, HTTP streams,
/// recognizer callbacks), so file writes and the buffer are guarded by a single gate.
/// File I/O failures are swallowed — the UI still receives the entry.
/// </summary>
public sealed class Logger : ILogger
{
    // Cap the in-memory history so a long session can't grow unbounded; the file keeps everything.
    private const int MaxInMemory = 2000;

    private readonly object _gate = new();
    private readonly Queue<LogEntry> _buffer = new();
    private readonly string _dir;
    private readonly string _path;

    public event Action<LogEntry>? EntryLogged;

    public Logger()
    {
        _dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "raven_ai", "logs");
        _path = Path.Combine(_dir, $"raven_ai-{DateTime.Now:yyyyMMdd}.log");
    }

    /// <summary>Absolute path of today's log file (shown in the panel and used by "Open folder").</summary>
    public string LogFilePath => _path;

    public IReadOnlyList<LogEntry> Snapshot()
    {
        lock (_gate) return _buffer.ToArray();
    }

    public void Log(LogLevel level, string message, Exception? exception = null, string category = "App")
    {
        var entry = new LogEntry(DateTimeOffset.Now, level, category, message, exception?.ToString());

        lock (_gate)
        {
            _buffer.Enqueue(entry);
            while (_buffer.Count > MaxInMemory) _buffer.Dequeue();
            TryAppendToFile(entry);
        }

        // Raised outside the lock: UI handlers marshal to the dispatcher and must never be able
        // to stall other threads that are trying to log.
        EntryLogged?.Invoke(entry);
    }

    private void TryAppendToFile(LogEntry entry)
    {
        try
        {
            Directory.CreateDirectory(_dir);
            File.AppendAllText(_path, entry.ToFileLine() + Environment.NewLine, Encoding.UTF8);
        }
        catch
        {
            // A logger that throws is worse than a missing file line. Swallow and keep going.
        }
    }
}
