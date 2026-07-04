using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RavenAI.Services.Voice;

namespace RavenAI.ViewModels;

/// <summary>
/// One independent live-transcription channel: wraps a single <see cref="AzureSpeechRecognizer"/>
/// bound to one audio source — the interviewee's microphone, or the interviewer's system audio
/// ("what you hear") — with its own start/stop, status, and transcript. The two channels run
/// concurrently and never share state. Recognizer events arrive on background threads, so every
/// observable update is marshalled to the UI dispatcher.
/// </summary>
public sealed partial class SpeechChannelViewModel : ObservableObject, IDisposable
{
    private readonly AzureSpeechRecognizer _recognizer;
    private readonly AudioInputSource _source;
    private readonly Dispatcher _dispatcher;
    private readonly StringBuilder _final = new();

    public SpeechChannelViewModel(string title, AzureSpeechRecognizer recognizer, AudioInputSource source)
    {
        Title = title;
        _recognizer = recognizer;
        _source = source;
        _dispatcher = Application.Current.Dispatcher;

        _recognizer.InterimResult += OnInterim;
        _recognizer.FinalResult += OnFinal;
        _recognizer.ErrorOccurred += OnError;
        _recognizer.Started += OnStarted;
        _recognizer.Stopped += OnStopped;

        if (SupportsDeviceSelection)
            RefreshMicrophones();
    }

    /// <summary>Display name for the channel (e.g. "You" or "Interviewer").</summary>
    public string Title { get; }

    /// <summary>Only the microphone channel exposes a device picker; system audio has no device to choose.</summary>
    public bool SupportsDeviceSelection => _source == AudioInputSource.Microphone;

    // ---- Microphone device picker (microphone channel only) ----------------------------

    /// <summary>Available microphones (default entry first). Populated at startup and on refresh.</summary>
    public ObservableCollection<AudioInputDevice> Microphones { get; } = new();

    /// <summary>The microphone this channel listens to. Null-Id entry means the system default.</summary>
    [ObservableProperty] private AudioInputDevice? _selectedMicrophone;

    /// <summary>Device selection is locked while listening; a restart is needed to switch mics.</summary>
    public bool CanSelectMicrophone => !IsListening;

    // Re-query the active capture endpoints, preserving the current selection when it still exists
    // (else falling back to the default entry).
    [RelayCommand]
    private void RefreshMicrophones()
    {
        string? previousId = SelectedMicrophone?.Id;
        Microphones.Clear();
        foreach (var device in MicrophoneEnumerator.List())
            Microphones.Add(device);

        SelectedMicrophone = Microphones.FirstOrDefault(d => d.Id == previousId)
            ?? Microphones.FirstOrDefault();
    }

    // ---- Channel state -----------------------------------------------------------------

    [ObservableProperty] private bool _isListening;
    partial void OnIsListeningChanged(bool value) => OnPropertyChanged(nameof(CanSelectMicrophone));

    [ObservableProperty] private string _status = "Idle";
    [ObservableProperty] private string _interimText = string.Empty;
    [ObservableProperty] private string _finalTranscript = string.Empty;

    // Disable the button while start/stop is in flight so a double-click can't enter the async
    // path twice and create a second recognizer (leaking the first).
    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task ToggleAsync()
    {
        if (IsListening)
        {
            Status = "Stopping…";
            try { await _recognizer.StopAsync(); }
            catch (Exception ex) { Status = $"Error: {ex.Message}"; }
            return;
        }

        Status = "Starting…";
        try { await _recognizer.StartAsync(_source, SelectedMicrophone?.Id); }
        catch (Exception ex) { Status = $"Error: {ex.Message}"; }
    }

    [RelayCommand]
    private void Clear()
    {
        _final.Clear();
        FinalTranscript = string.Empty;
        InterimText = string.Empty;
    }

    // ---- Recognizer event handlers (background threads → marshal to UI) -----------------

    private void OnInterim(string text) => _dispatcher.Invoke(() => InterimText = text);

    private void OnFinal(string text) => _dispatcher.Invoke(() =>
    {
        _final.AppendLine(text);
        InterimText = string.Empty;
        FinalTranscript = _final.ToString();
    });

    private void OnError(string message) => _dispatcher.Invoke(() => Status = $"Error: {message}");

    private void OnStarted() => _dispatcher.Invoke(() =>
    {
        IsListening = true;
        Status = "Listening";
    });

    private void OnStopped() => _dispatcher.Invoke(() =>
    {
        IsListening = false;
        InterimText = string.Empty;
        Status = "Idle";
    });

    public void Dispose()
    {
        _recognizer.InterimResult -= OnInterim;
        _recognizer.FinalResult -= OnFinal;
        _recognizer.ErrorOccurred -= OnError;
        _recognizer.Started -= OnStarted;
        _recognizer.Stopped -= OnStopped;
        _recognizer.Dispose();
    }
}
