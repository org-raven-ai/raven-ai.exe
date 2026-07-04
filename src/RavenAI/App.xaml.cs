using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using RavenAI.Models;
using RavenAI.Services;
using RavenAI.Services.Chat;
using RavenAI.Services.Chat.Tools;
using RavenAI.Services.Logging;
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
    private SingleInstanceGuard? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // --- Unified logger: file + in-app panel. Set up first so everything after it can log. ---
        var logger = new Logger();
        Log.Init(logger);
        HookGlobalExceptionHandlers();
        Log.Info("raven_ai starting", "App");

        // --- Single instance: if we're not the first, wake the running window and exit. ---
        _singleInstance = new SingleInstanceGuard();
        if (!_singleInstance.IsFirstInstance)
        {
            Log.Info("Another instance is already running — surfacing it and exiting.", "App");
            _singleInstance.SignalExistingInstance();
            Shutdown();
            return;
        }

        _store = new SecureSettingsStore();
        _settings = _store.Load();

        // --- Model catalog: lists /models for the Settings dropdowns (fresh key + base URL) ---
        var modelCatalog = new OpenAIModelCatalog(() =>
        (
            apiKey: _store.GetAPIKey(_settings) ?? string.Empty,
            baseURL: _settings.BaseURL
        ));

        // --- Voice: STT + TTS (provider or offline fallback, chosen at speak time) ---
        var stt = new OpenAISpeechToText(() =>
        (
            apiKey: _store.GetAPIKey(_settings) ?? string.Empty,
            baseURL: _settings.BaseURL,
            model: _settings.STTModel
        ));

        Func<ITextToSpeech> ttsFactory = () =>
        {
            if (_settings.UseOfflineTTS)
                return new OfflineTextToSpeech();
            return new OpenAITextToSpeech(() =>
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

        // --- Tools the chat model can call (registry is trivially extensible) ---
        // Local/offline (time, calculator), web search (Tavily key from Settings), and app context
        // (the live Azure Speech transcript). Each reads fresh config/state per call.
        var toolRegistry = new ChatToolRegistry(new IChatTool[]
        {
            new CurrentTimeTool(),
            new CalculatorTool(),
            new WebSearchTool(_http, () =>
            (
                apiKey: _store.GetWebSearchKey(_settings) ?? string.Empty,
                endpoint: _settings.WebSearchEndpoint
            )),
            new TranscriptTool(() =>
            (
                you: speechVm.You.FinalTranscript,
                interviewer: speechVm.Interviewer.FinalTranscript
            )),
        });

        // --- Chat provider (reads fresh config + decrypts key per request; runs the tool loop) ---
        var chatProvider = new OpenAIChatProvider(() =>
        (
            apiKey: _store.GetAPIKey(_settings) ?? string.Empty,
            baseURL: _settings.BaseURL,
            model: _settings.Model,
            systemPrompt: _settings.SystemPrompt
        ), toolRegistry);

        var chatVm = new ChatViewModel(chatProvider, stt, ttsFactory, capture);
        var settingsVm = new SettingsViewModel(_store, _settings, modelCatalog);
        var logVm = new LogViewModel(logger);
        var mainVm = new MainViewModel(chatVm, settingsVm, speechVm, logVm);

        var window = new MainWindow(mainVm, new ScreenCaptureProtectionService(),
                                    new GlobalHotkeyService(), capture,
                                    new Services.Overlay.InteractiveModeController());

        // A later launch signals us on a thread-pool thread; marshal onto the UI thread to
        // un-minimize, center, and foreground the existing window.
        _singleInstance.ListenForActivation(() =>
            window.Dispatcher.Invoke(window.SurfaceFromOtherInstance));

        window.Show();
    }

    /// <summary>
    /// Routes every unhandled exception in the process to the unified logger so it lands in both
    /// the log file and the in-app panel. The dispatcher handler marks the exception handled so a
    /// UI-thread fault doesn't tear the overlay down — the error is surfaced in the log instead.
    /// </summary>
    private void HookGlobalExceptionHandlers()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error("Unhandled UI exception", args.Exception, "Unhandled");
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Error("Unhandled exception", args.ExceptionObject as Exception, "Unhandled");

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error("Unobserved task exception", args.Exception, "Unhandled");
            args.SetObserved();
        };
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Info("raven_ai exiting", "App");
        _singleInstance?.Dispose();
        _http.Dispose();
        base.OnExit(e);
    }
}
