namespace RavenAi.Models;

/// <summary>
/// User-configurable settings persisted to %APPDATA%\raven_ai\settings.json.
///
/// IMPORTANT: <see cref="EncryptedApiKey"/> holds a DPAPI-protected, base64-encoded blob —
/// never a plaintext key. The plaintext key only ever lives transiently in memory while a
/// request is being built. See <see cref="Services.SecureSettingsStore"/>.
/// </summary>
public sealed class RavenAiSettings
{
    /// <summary>DPAPI-protected (CurrentUser scope), base64-encoded API key. Never plaintext.</summary>
    public string? EncryptedApiKey { get; set; }

    /// <summary>OpenAI-compatible base URL, e.g. https://api.openai.com/v1</summary>
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";

    /// <summary>Chat model id, e.g. gpt-4o-mini.</summary>
    public string Model { get; set; } = "gpt-4o-mini";

    /// <summary>Transcription (speech-to-text) model id.</summary>
    public string SttModel { get; set; } = "whisper-1";

    /// <summary>Text-to-speech model id.</summary>
    public string TtsModel { get; set; } = "gpt-4o-mini-tts";

    /// <summary>TTS voice name.</summary>
    public string TtsVoice { get; set; } = "alloy";

    /// <summary>Optional system prompt prepended to every conversation.</summary>
    public string SystemPrompt { get; set; } = "You are raven_ai, a concise and helpful assistant.";

    /// <summary>Use the Windows built-in offline TTS instead of the provider's TTS endpoint.</summary>
    public bool UseOfflineTts { get; set; }

    /// <summary>
    /// Whole-window opacity in percent (30–100). Applied via SetLayeredWindowAttributes
    /// (LWA_ALPHA), never WPF AllowsTransparency, so screen-capture exclusion keeps working.
    /// </summary>
    public int WindowOpacityPercent { get; set; } = 100;
}
