using System.Text;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RavenAI.Services.Voice;

namespace RavenAI.ViewModels;

/// <summary>
/// Backs the standalone Speech-to-Text panel: picks mic vs system audio, toggles live Azure
/// recognition on/off, and shows the interim + final transcript. Recognizer events arrive on
/// background threads, so every observable update is marshalled to the UI dispatcher.
/// </summary>
public sealed partial class SpeechViewModel : ObservableObject, IDisposable
{
    private readonly AzureSpeechRecognizer _recognizer;
    private readonly Dispatcher _dispatcher;
    private readonly StringBuilder _final = new();

    private AudioInputSource _inputSource = AudioInputSource.Microphone;

    public SpeechViewModel(AzureSpeechRecognizer recognizer)
    {
        _recognizer = recognizer;
        _dispatcher = Application.Current.Dispatcher;

        _recognizer.InterimResult += OnInterim;
        _recognizer.FinalResult += OnFinal;
        _recognizer.ErrorOccurred += OnError;
        _recognizer.Started += OnStarted;
        _recognizer.Stopped += OnStopped;
    }

    /// <summary>Currently selected audio input.</summary>
    public AudioInputSource InputSource
    {
        get => _inputSource;
        private set
        {
            if (SetProperty(ref _inputSource, value))
            {
                OnPropertyChanged(nameof(IsMicrophone));
                OnPropertyChanged(nameof(IsSystemAudio));
            }
        }
    }

    // Two-way radio-button bindings. Only react when a radio becomes checked (value == true);
    // the sibling radio firing false during the mutual-exclusion cascade is ignored.
    public bool IsMicrophone
    {
        get => InputSource == AudioInputSource.Microphone;
        set { if (value) InputSource = AudioInputSource.Microphone; }
    }

    public bool IsSystemAudio
    {
        get => InputSource == AudioInputSource.SystemAudio;
        set { if (value) InputSource = AudioInputSource.SystemAudio; }
    }

    [ObservableProperty] private bool _isListening;
    [ObservableProperty] private string _status = "Idle";
    [ObservableProperty] private string _interimText = string.Empty;
    [ObservableProperty] private string _finalTranscript = string.Empty;

    [RelayCommand]
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
        try
        {
            await _recognizer.StartAsync(InputSource);
        }
        catch (Exception ex)
        {
            Status = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private void Clear()
    {
        _final.Clear();
        FinalTranscript = string.Empty;
        InterimText = string.Empty;
    }

    // ---- Recognizer event handlers (background threads → marshal to UI) ----------------

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
        Status = InputSource == AudioInputSource.SystemAudio ? "Listening (system audio)" : "Listening";
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
