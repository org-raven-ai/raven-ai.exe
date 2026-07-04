using System.ClientModel;
using System.IO;
using OpenAI;
using OpenAI.Audio;

namespace RavenAI.Services.Voice;

/// <summary>
/// Speech-to-text via an OpenAI-compatible /audio/transcriptions endpoint, using the official
/// OpenAI .NET SDK (<see cref="AudioClient"/>). The API key, base URL, and model are read fresh per
/// call so the newest saved credentials (and provider) are always used.
/// </summary>
public sealed class OpenAISpeechToText : ISpeechToText
{
    private readonly Func<(string apiKey, string baseURL, string model)> _configProvider;

    public OpenAISpeechToText(Func<(string apiKey, string baseURL, string model)> configProvider)
    {
        _configProvider = configProvider;
    }

    public async Task<string> TranscribeAsync(byte[] wavAudio, CancellationToken cancellationToken = default)
    {
        var (apiKey, baseURL, model) = _configProvider();
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("No API key configured.");
        if (wavAudio.Length == 0)
            return string.Empty;

        AudioClient client = CreateClient(apiKey, baseURL, model);

        using var stream = new MemoryStream(wavAudio);
        AudioTranscription transcription = await client.TranscribeAudioAsync(stream, "speech.wav");
        return transcription.Text ?? string.Empty;
    }

    private static AudioClient CreateClient(string apiKey, string baseURL, string model)
    {
        var options = new OpenAIClientOptions();
        if (!string.IsNullOrWhiteSpace(baseURL))
            options.Endpoint = new Uri(baseURL);
        return new AudioClient(model, new ApiKeyCredential(apiKey), options);
    }
}
