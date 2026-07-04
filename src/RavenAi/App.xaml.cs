using System.Net.Http;
using System.Windows;
using RavenAi.Models;
using RavenAi.Services;
using RavenAi.Services.Chat;
using RavenAi.Services.Voice;
using RavenAi.ViewModels;
using RavenAi.Views;

namespace RavenAi;

/// <summary>
/// Composition root. Wires up services and view models by hand (no DI container — keeps the
/// app lightweight per the brief) and shows the main window.
/// </summary>
public partial class App : Application
{
    // Single shared HttpClient for the process lifetime.
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(5) };

    private SecureSettingsStore _store = null!;
    private RavenAiSettings _settings = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _store = new SecureSettingsStore();
        _settings = _store.Load();

        // --- Chat provider (reads fresh config + decrypts key per request) ---
        var chatProvider = new OpenAiChatProvider(_http, () =>
        (
            apiKey: _store.GetApiKey(_settings) ?? string.Empty,
            baseUrl: _settings.BaseUrl,
            model: _settings.Model,
            systemPrompt: _settings.SystemPrompt
        ));

        // --- Voice: STT + TTS (provider or offline fallback, chosen at speak time) ---
        var stt = new OpenAiSpeechToText(_http, () =>
        (
            apiKey: _store.GetApiKey(_settings) ?? string.Empty,
            baseUrl: _settings.BaseUrl,
            model: _settings.SttModel
        ));

        Func<ITextToSpeech> ttsFactory = () =>
        {
            if (_settings.UseOfflineTts)
                return new OfflineTextToSpeech();
            return new OpenAiTextToSpeech(_http, () =>
            (
                apiKey: _store.GetApiKey(_settings) ?? string.Empty,
                baseUrl: _settings.BaseUrl,
                model: _settings.TtsModel,
                voice: _settings.TtsVoice
            ));
        };

        var capture = new NAudioCapture();

        var chatVm = new ChatViewModel(chatProvider, stt, ttsFactory, capture);
        var settingsVm = new SettingsViewModel(_store, _settings);
        var mainVm = new MainViewModel(chatVm, settingsVm);

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
