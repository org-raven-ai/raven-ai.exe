using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RavenAI.Models;
using RavenAI.Services;

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

    public SettingsViewModel(SecureSettingsStore store, RavenAISettings settings)
    {
        _store = store;
        _settings = settings;

        _baseURL = settings.BaseURL;
        _model = settings.Model;
        _sttModel = settings.STTModel;
        _ttsModel = settings.TTSModel;
        _ttsVoice = settings.TTSVoice;
        _systemPrompt = settings.SystemPrompt;
        _useOfflineTTS = settings.UseOfflineTTS;
        _windowOpacity = Math.Clamp(settings.WindowOpacityPercent, 30, 100);
        _hasStoredKey = !string.IsNullOrEmpty(settings.EncryptedAPIKey);
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

    /// <summary>Whole-window opacity in percent (30–100). Applied live as the slider moves.</summary>
    [ObservableProperty] private int _windowOpacity;

    [ObservableProperty] private string _statusMessage = string.Empty;

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

        // Only overwrite the stored key if the user typed something new.
        if (!string.IsNullOrWhiteSpace(APIKeyInput))
        {
            _store.SetAPIKey(_settings, APIKeyInput.Trim());
            HasStoredKey = true;
        }

        _store.Save(_settings);

        APIKeyInput = string.Empty; // never keep plaintext around
        StatusMessage = "Settings saved.";
        Saved?.Invoke();
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
}
