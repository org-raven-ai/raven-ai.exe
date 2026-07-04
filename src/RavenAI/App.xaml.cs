using System.Net.Http;
using System.Windows;
using RavenAI.Models;
using RavenAI.Services;
using RavenAI.Services.Chat;
using RavenAI.Services.Voice;
using RavenAI.ViewModels;
using RavenAI.Views;

namespace RavenAI;

/// <summary>
/// Composition root. Wires up services and view models by hand (no DI container — keeps the
/// app lightweight per the brief) and shows the main window.
/// </summary>
public partial class App : Application
{
    // Single shared HttpClient for the process lifetime.
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(5) };

    private SecureSettingsStore _store = null!;
    private RavenAISettings _settings = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _store = new SecureSettingsStore();
        _settings = _store.Load();

        // --- Chat provider (reads fresh config + decrypts key per request) ---
        var chatProvider = new OpenAIChatProvider(_http, () =>
        (
            apiKey: _store.GetAPIKey(_settings) ?? string.Empty,
            baseURL: _settings.BaseURL,
            model: _settings.Model,
            systemPrompt: _settings.SystemPrompt
        ));

        // --- Voice: STT + TTS (provider or offline fallback, chosen at speak time) ---
        var stt = new OpenAISpeechToText(_http, () =>
        (
            apiKey: _store.GetAPIKey(_settings) ?? string.Empty,
            baseURL: _settings.BaseURL,
            model: _settings.STTModel
        ));

        Func<ITextToSpeech> ttsFactory = () =>
        {
            if (_settings.UseOfflineTTS)
                return new OfflineTextToSpeech();
            return new OpenAITextToSpeech(_http, () =>
            (
                apiKey: _store.GetAPIKey(_settings) ?? string.Empty,
                baseURL: _settings.BaseURL,
                model: _settings.TTSModel,
                voice: _settings.TTSVoice
            ));
        };

        var capture = new NAudioCapture();

        // --- Azure Speech (live speech-to-text panel) ---
        // Two independent recognizers sharing the same config: one for the interviewee's mic,
        // one for the interviewer's system audio. They run concurrently.
        Func<(string apiKey, string endpoint, string language)> azureSpeechConfig = () =>
        (
            apiKey: _store.GetAzureSpeechKey(_settings) ?? string.Empty,
            endpoint: _settings.AzureSpeechEndpoint,
            language: _settings.AzureSpeechRecognitionLanguage
        );
        var micRecognizer = new AzureSpeechRecognizer(azureSpeechConfig);
        var systemAudioRecognizer = new AzureSpeechRecognizer(azureSpeechConfig);
        var speechVm = new SpeechViewModel(micRecognizer, systemAudioRecognizer);

        var chatVm = new ChatViewModel(chatProvider, stt, ttsFactory, capture);
        var settingsVm = new SettingsViewModel(_store, _settings);
        var mainVm = new MainViewModel(chatVm, settingsVm, speechVm);

        var window = new MainWindow(mainVm, new ScreenCaptureProtectionService(),
                                    new GlobalHotkeyService(), capture);
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _http.Dispose();
        base.OnExit(e);
    }
}
