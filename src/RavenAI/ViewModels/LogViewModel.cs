using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RavenAI.Services.Logging;

namespace RavenAI.ViewModels;

/// <summary>
/// Backs the in-app log panel. Mirrors the unified <see cref="Logger"/>'s entries into an
/// observable collection: seeds from the existing snapshot, then appends live as new entries are
/// logged. Logger events arrive on arbitrary background threads, so every append is marshalled to
/// the UI dispatcher.
/// </summary>
public sealed partial class LogViewModel : ObservableObject
{
    // Keep the visible list bounded (the file retains the full history).
    private const int MaxVisible = 2000;

    private readonly Logger _logger;
    private readonly Dispatcher _dispatcher;

    /// <summary>Entries shown in the panel, oldest first.</summary>
    public ObservableCollection<LogEntry> Entries { get; } = new();

    /// <summary>Path of the file every entry is also written to (shown under the panel).</summary>
    public string LogFilePath { get; }

    public LogViewModel(Logger logger)
    {
        _logger = logger;
        _dispatcher = Application.Current.Dispatcher;
        LogFilePath = logger.LogFilePath;

        foreach (LogEntry entry in logger.Snapshot())
            Entries.Add(entry);

        _logger.EntryLogged += OnEntryLogged;
    }

    private void OnEntryLogged(LogEntry entry) => _dispatcher.BeginInvoke(() =>
    {
        Entries.Add(entry);
        while (Entries.Count > MaxVisible)
            Entries.RemoveAt(0);
    });

    [RelayCommand]
    private void Clear() => Entries.Clear();

    [RelayCommand]
    private void CopyAll()
    {
        var sb = new StringBuilder();
        foreach (LogEntry entry in Entries)
            sb.AppendLine(entry.ToFileLine());
        try { System.Windows.Clipboard.SetText(sb.ToString()); }
        catch (Exception ex) { Log.Warning("Could not copy logs to clipboard", ex, "Logs"); }
    }

    [RelayCommand]
    private void OpenLogFolder()
    {
        try
        {
            string? folder = Path.GetDirectoryName(LogFilePath);
            if (folder is not null)
            {
                Directory.CreateDirectory(folder);
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
            }
        }
        catch (Exception ex) { Log.Warning("Could not open the log folder", ex, "Logs"); }
    }
}
