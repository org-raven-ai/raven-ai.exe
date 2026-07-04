using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RavenAi.Models;
using RavenAi.Services;

namespace RavenAi.ViewModels;

/// <summary>
/// Backs the Settings view. Editable copies of the settings live here; the API key text box
/// binds to <see cref="ApiKeyInput"/> which is never persisted in plaintext — on Save it is
/// DPAPI-encrypted by <see cref="SecureSettingsStore"/>.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly SecureSettingsStore _store;
    private readonly RavenAiSettings _settings;

    public SettingsViewModel(SecureSettingsStore store, RavenAiSettings settings)
    {
        _store = store;
        _settings = settings;

        _baseUrl = settings.BaseUrl;
        _model = settings.Model;
        _sttModel = settings.SttModel;
        _ttsModel = settings.TtsModel;
        _ttsVoice = settings.TtsVoice;
        _systemPrompt = settings.SystemPrompt;
        _useOfflineTts = settings.UseOfflineTts;
        _hasStoredKey = !string.IsNullOrEmpty(settings.EncryptedApiKey);
    }

    // Plaintext only in this box, only while the user is typing. Cleared after Save.
    [ObservableProperty] private string _apiKeyInput = string.Empty;
    [ObservableProperty] private bool _hasStoredKey;

    [ObservableProperty] private string _baseUrl;
    [ObservableProperty] private string _model;
    [ObservableProperty] private string _sttModel;
    [ObservableProperty] private string _ttsModel;
    [ObservableProperty] private string _ttsVoice;
    [ObservableProperty] private string _systemPrompt;
    [ObservableProperty] private bool _useOfflineTts;

    [ObservableProperty] private string _statusMessage = string.Empty;

    /// <summary>Raised after a successful save so the shell can hide the settings pane.</summary>
    public event Action? Saved;

    [RelayCommand]
    private void Save()
    {
        _settings.BaseUrl = (BaseUrl ?? string.Empty).Trim();
        _settings.Model = (Model ?? string.Empty).Trim();
        _settings.SttModel = (SttModel ?? string.Empty).Trim();
        _settings.TtsModel = (TtsModel ?? string.Empty).Trim();
        _settings.TtsVoice = (TtsVoice ?? string.Empty).Trim();
        _settings.SystemPrompt = SystemPrompt ?? string.Empty;
        _settings.UseOfflineTts = UseOfflineTts;

        // Only overwrite the stored key if the user typed something new.
        if (!string.IsNullOrWhiteSpace(ApiKeyInput))
        {
            _store.SetApiKey(_settings, ApiKeyInput.Trim());
            HasStoredKey = true;
        }

        _store.Save(_settings);

        ApiKeyInput = string.Empty; // never keep plaintext around
        StatusMessage = "Settings saved.";
        Saved?.Invoke();
    }

    [RelayCommand]
    private void ClearKey()
    {
        _store.SetApiKey(_settings, null);
        _store.Save(_settings);
        HasStoredKey = false;
        ApiKeyInput = string.Empty;
        StatusMessage = "Stored API key cleared.";
    }
}
