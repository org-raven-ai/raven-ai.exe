namespace RavenAI.Models;

/// <summary>
/// User-configurable settings persisted to %APPDATA%\raven_ai\settings.json.
///
/// IMPORTANT: <see cref="EncryptedAPIKey"/> holds a DPAPI-protected, base64-encoded blob —
/// never a plaintext key. The plaintext key only ever lives transiently in memory while a
/// request is being built. See <see cref="Services.SecureSettingsStore"/>.
/// </summary>
public sealed class RavenAISettings
{
    /// <summary>DPAPI-protected (CurrentUser scope), base64-encoded API key. Never plaintext.</summary>
    public string? EncryptedAPIKey { get; set; }

    /// <summary>OpenAI-compatible base URL, e.g. https://api.openai.com/v1</summary>
    public string BaseURL { get; set; } = "https://api.openai.com/v1";

    /// <summary>Chat model id, e.g. gpt-4o-mini.</summary>
    public string Model { get; set; } = "gpt-4o-mini";

    /// <summary>Transcription (speech-to-text) model id.</summary>
    public string STTModel { get; set; } = "whisper-1";

    /// <summary>Text-to-speech model id.</summary>
    public string TTSModel { get; set; } = "gpt-4o-mini-tts";

    /// <summary>TTS voice name.</summary>
    public string TTSVoice { get; set; } = "alloy";

    /// <summary>Optional system prompt prepended to every conversation.</summary>
    public string SystemPrompt { get; set; } = "You are raven_ai, a concise and helpful assistant.";

    /// <summary>Use the Windows built-in offline TTS instead of the provider's TTS endpoint.</summary>
    public bool UseOfflineTTS { get; set; }

    /// <summary>
    /// DPAPI-protected (CurrentUser scope), base64-encoded Azure Speech resource key. Separate
    /// from <see cref="EncryptedAPIKey"/> because Azure Speech is an independent credential.
    /// Never plaintext; see <see cref="Services.SecureSettingsStore"/>.
    /// </summary>
    public string? EncryptedAzureSpeechApiKey { get; set; }

    /// <summary>
    /// Azure Speech endpoint URL (e.g. https://eastus.api.cognitive.microsoft.com) OR a bare
    /// region name (e.g. eastus). The recognizer picks FromEndpoint vs FromSubscription accordingly.
    /// </summary>
    public string AzureSpeechEndpoint { get; set; } = string.Empty;

    /// <summary>BCP-47 recognition language for Azure Speech, e.g. en-US.</summary>
    public string AzureSpeechRecognitionLanguage { get; set; } = "en-US";

    /// <summary>
    /// Whole-window opacity in percent (30–100), applied live via WPF Window.Opacity.
    /// Screen-capture exclusion still holds while translucent (verified at runtime; the
    /// protection watchdog and warning banner are the safety net).
    /// </summary>
    public int WindowOpacityPercent { get; set; } = 100;
}
