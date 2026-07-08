using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RavenAI.Models;
using RavenAI.Services;
using RavenAI.Services.Chat;
using RavenAI.Services.Logging;

namespace RavenAI.ViewModels;

/// <summary>
/// Backs the Settings view. Editable copies of the settings live here; the API key text box
/// binds to <see cref="APIKeyInput"/> which is never persisted in plaintext — on Save it is
/// DPAPI-encrypted by <see cref="SecureSettingsStore"/>.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly SecureSettingsStore _store;
    private readonly RavenAISettings _settings;
    private readonly OpenAIModelCatalog _modelCatalog;

    public SettingsViewModel(SecureSettingsStore store, RavenAISettings settings, OpenAIModelCatalog modelCatalog)
    {
        _store = store;
        _settings = settings;
        _modelCatalog = modelCatalog;

        _baseURL = settings.BaseURL;
        _model = settings.Model;
        _sttModel = settings.STTModel;
        _ttsModel = settings.TTSModel;
        _ttsVoice = settings.TTSVoice;
        _systemPrompt = settings.SystemPrompt;
        _useOfflineTTS = settings.UseOfflineTTS;
        _windowOpacity = Math.Clamp(settings.WindowOpacityPercent, 30, 100);
        _hasStoredKey = !string.IsNullOrEmpty(settings.EncryptedAPIKey);
        _azureSpeechEndpoint = settings.AzureSpeechEndpoint;
        _azureSpeechRecognitionLanguage = settings.AzureSpeechRecognitionLanguage;
        _hasStoredAzureSpeechKey = !string.IsNullOrEmpty(settings.EncryptedAzureSpeechApiKey);
        _webSearchEndpoint = settings.WebSearchEndpoint;
        _hasStoredWebSearchKey = !string.IsNullOrEmpty(settings.EncryptedWebSearchApiKey);
    }

    // These are manual properties rather than [ObservableProperty] because the MVVM source
    // generator only upper-cases the first letter of the field name (e.g. _apiKeyInput ->
    // ApiKeyInput), which breaks the abbreviation-casing rule (APIKeyInput, STTModel, TTSModel,
    // TTSVoice). SetProperty gives the same change-notification behaviour with the correct name.
    private string _apiKeyInput = string.Empty; // plaintext only while typing; cleared after Save
    public string APIKeyInput { get => _apiKeyInput; set => SetProperty(ref _apiKeyInput, value); }

    private string _sttModel = string.Empty;
    public string STTModel { get => _sttModel; set => SetProperty(ref _sttModel, value); }

    private string _ttsModel = string.Empty;
    public string TTSModel { get => _ttsModel; set => SetProperty(ref _ttsModel, value); }

    private string _ttsVoice = string.Empty;
    public string TTSVoice { get => _ttsVoice; set => SetProperty(ref _ttsVoice, value); }

    [ObservableProperty] private bool _hasStoredKey;
    [ObservableProperty] private string _baseURL;
    [ObservableProperty] private string _model;
    [ObservableProperty] private string _systemPrompt;
    [ObservableProperty] private bool _useOfflineTTS;

    // Azure Speech (speech-to-text) — independent credential from the OpenAI key above.
    [ObservableProperty] private bool _hasStoredAzureSpeechKey;
    private string _azureSpeechKeyInput = string.Empty; // plaintext only while typing; cleared after Save
    public string AzureSpeechKeyInput { get => _azureSpeechKeyInput; set => SetProperty(ref _azureSpeechKeyInput, value); }
    [ObservableProperty] private string _azureSpeechEndpoint;
    [ObservableProperty] private string _azureSpeechRecognitionLanguage;

    // Web search (Tavily) — independent credential, powers the assistant's web_search tool.
    [ObservableProperty] private bool _hasStoredWebSearchKey;
    private string _webSearchKeyInput = string.Empty; // plaintext only while typing; cleared after Save
    public string WebSearchKeyInput { get => _webSearchKeyInput; set => SetProperty(ref _webSearchKeyInput, value); }
    [ObservableProperty] private string _webSearchEndpoint;

    /// <summary>Whole-window opacity in percent (30–100). Applied live as the slider moves.</summary>
    [ObservableProperty] private int _windowOpacity;

    [ObservableProperty] private string _statusMessage = string.Empty;

    /// <summary>
    /// Model ids fetched from the provider's /models endpoint. Shared by the Chat / STT / TTS
    /// model dropdowns; the fields stay editable so a custom id can still be typed.
    /// </summary>
    public ObservableCollection<string> AvailableModels { get; } = new();

    /// <summary>True while a /models fetch is in flight (drives the "Loading…" status).</summary>
    [ObservableProperty] private bool _isLoadingModels;

    /// <summary>Raised after a successful save so the shell can hide the settings pane.</summary>
    public event Action? Saved;

    /// <summary>Raised whenever the opacity slider moves so the window can apply it live.</summary>
    public event Action<int>? WindowOpacityChanged;

    partial void OnWindowOpacityChanged(int value) => WindowOpacityChanged?.Invoke(value);

    [RelayCommand]
    private void Save()
    {
        _settings.BaseURL = (BaseURL ?? string.Empty).Trim();
        _settings.Model = (Model ?? string.Empty).Trim();
        _settings.STTModel = (STTModel ?? string.Empty).Trim();
        _settings.TTSModel = (TTSModel ?? string.Empty).Trim();
        _settings.TTSVoice = (TTSVoice ?? string.Empty).Trim();
        _settings.SystemPrompt = SystemPrompt ?? string.Empty;
        _settings.UseOfflineTTS = UseOfflineTTS;
        _settings.WindowOpacityPercent = Math.Clamp(WindowOpacity, 30, 100);
        _settings.AzureSpeechEndpoint = (AzureSpeechEndpoint ?? string.Empty).Trim();
        _settings.AzureSpeechRecognitionLanguage = (AzureSpeechRecognitionLanguage ?? string.Empty).Trim();
        _settings.WebSearchEndpoint = (WebSearchEndpoint ?? string.Empty).Trim();

        // Only overwrite the stored key if the user typed something new.
        if (!string.IsNullOrWhiteSpace(APIKeyInput))
        {
            _store.SetAPIKey(_settings, APIKeyInput.Trim());
            HasStoredKey = true;
        }

        if (!string.IsNullOrWhiteSpace(AzureSpeechKeyInput))
        {
            _store.SetAzureSpeechKey(_settings, AzureSpeechKeyInput.Trim());
            HasStoredAzureSpeechKey = true;
        }

        if (!string.IsNullOrWhiteSpace(WebSearchKeyInput))
        {
            _store.SetWebSearchKey(_settings, WebSearchKeyInput.Trim());
            HasStoredWebSearchKey = true;
        }

        _store.Save(_settings);

        APIKeyInput = string.Empty; // never keep plaintext around
        AzureSpeechKeyInput = string.Empty;
        WebSearchKeyInput = string.Empty;
        StatusMessage = "Settings saved.";
        Saved?.Invoke();
    }

    /// <summary>
    /// Fetches the provider's model list into <see cref="AvailableModels"/>. Uses the currently
    /// stored key (typing a new key in the box requires a Save first). Failures are surfaced in
    /// the status line and logged rather than thrown.
    /// </summary>
    [RelayCommand]
    private async Task RefreshModelsAsync()
    {
        IsLoadingModels = true;
        StatusMessage = "Loading models…";
        try
        {
            IReadOnlyList<string> models = await _modelCatalog.ListModelsAsync();
            AvailableModels.Clear();
            foreach (string m in models)
                AvailableModels.Add(m);
            StatusMessage = models.Count > 0
                ? $"Loaded {models.Count} models."
                : "The provider returned no models.";
        }
        catch (Exception ex)
        {
            Log.Warning("Failed to list models from the provider", ex, "Settings");
            StatusMessage = $"Could not load models: {ex.Message}";
        }
        finally
        {
            IsLoadingModels = false;
        }
    }

    /// <summary>
    /// Populates the model dropdowns the first time the Settings pane is shown, provided a key is
    /// stored and nothing has been loaded yet. No-op otherwise, so opening Settings offline (or a
    /// second time) stays instant and never blocks on the network.
    /// </summary>
    public void EnsureModelsLoaded()
    {
        if (AvailableModels.Count == 0 && HasStoredKey && !IsLoadingModels)
            RefreshModelsCommand.Execute(null);
    }

    // ---- First-run provider gate ---------------------------------------------------------
    // The gate blocks the overlay until a provider key is stored. Validate persists the
    // URL + key, then proves them by listing /models; Unlock (in the shell) closes the gate.

    /// <summary>True once the gate's key has been verified against the provider.</summary>
    [ObservableProperty] private bool _gateKeyValidated;

    /// <summary>True when <see cref="GateStatus"/> describes a failure (styles the line critical).</summary>
    [ObservableProperty] private bool _gateStatusIsError;

    /// <summary>Progress / verdict line under the gate's key field.</summary>
    [ObservableProperty] private string _gateStatus = string.Empty;

    /// <summary>True while the gate's /models probe is in flight.</summary>
    [ObservableProperty] private bool _isValidatingGate;

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task ValidateGateAsync()
    {
        string url = (BaseURL ?? string.Empty).Trim();
        string key = APIKeyInput.Trim();

        GateKeyValidated = false;
        GateStatusIsError = false;

        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            GateStatus = "Enter a valid https:// base URL first.";
            GateStatusIsError = true;
            return;
        }
        if (key.Length == 0 && !HasStoredKey)
        {
            GateStatus = "Paste your API key first.";
            GateStatusIsError = true;
            return;
        }
        if (key.Length > 0 && key.Length < 8)
        {
            GateStatus = "That key looks too short — paste the full secret.";
            GateStatusIsError = true;
            return;
        }

        // Persist the URL + key up front: the model catalog reads the stored settings.
        _settings.BaseURL = url;
        if (key.Length > 0)
        {
            _store.SetAPIKey(_settings, key);
            HasStoredKey = true;
        }
        _store.Save(_settings);
        APIKeyInput = string.Empty; // never keep plaintext around

        IsValidatingGate = true;
        GateStatus = $"Reaching {url.Replace("https://", string.Empty).Replace("http://", string.Empty)} …";
        try
        {
            IReadOnlyList<string> models = await _modelCatalog.ListModelsAsync();
            AvailableModels.Clear();
            foreach (string m in models)
                AvailableModels.Add(m);
            GateKeyValidated = true;
            GateStatus = models.Count > 0
                ? $"Verified · stored encrypted · {models.Count} models found"
                : "Verified · stored encrypted";
        }
        catch (Exception ex)
        {
            Log.Warning("Gate key validation failed", ex, "Settings");
            GateStatusIsError = true;
            GateStatus = $"Could not reach the provider: {ex.Message}";
        }
        finally
        {
            IsValidatingGate = false;
        }
    }

    [RelayCommand]
    private void ClearKey()
    {
        _store.SetAPIKey(_settings, null);
        _store.Save(_settings);
        HasStoredKey = false;
        APIKeyInput = string.Empty;
        StatusMessage = "Stored API key cleared.";
    }

    [RelayCommand]
    private void ClearAzureKey()
    {
        _store.SetAzureSpeechKey(_settings, null);
        _store.Save(_settings);
        HasStoredAzureSpeechKey = false;
        AzureSpeechKeyInput = string.Empty;
        StatusMessage = "Stored Azure Speech key cleared.";
    }

    [RelayCommand]
    private void ClearWebSearchKey()
    {
        _store.SetWebSearchKey(_settings, null);
        _store.Save(_settings);
        HasStoredWebSearchKey = false;
        WebSearchKeyInput = string.Empty;
        StatusMessage = "Stored web-search key cleared.";
    }
}
