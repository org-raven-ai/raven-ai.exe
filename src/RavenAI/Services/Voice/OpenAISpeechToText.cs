using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace RavenAI.Services.Voice;

/// <summary>
/// Speech-to-text via an OpenAI-compatible /audio/transcriptions endpoint (multipart upload).
/// </summary>
public sealed class OpenAISpeechToText : ISpeechToText
{
    private readonly HttpClient _http;
    private readonly Func<(string apiKey, string baseURL, string model)> _configProvider;

    public OpenAISpeechToText(HttpClient http, Func<(string apiKey, string baseURL, string model)> configProvider)
    {
        _http = http;
        _configProvider = configProvider;
    }

    public async Task<string> TranscribeAsync(byte[] wavAudio, CancellationToken cancellationToken = default)
    {
        var (apiKey, baseURL, model) = _configProvider();
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("No API key configured.");
        if (wavAudio.Length == 0)
            return string.Empty;

        string url = $"{baseURL.TrimEnd('/')}/audio/transcriptions";

        using var form = new MultipartFormDataContent();
        var audioContent = new ByteArrayContent(wavAudio);
        audioContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        form.Add(audioContent, "file", "speech.wav");
        form.Add(new StringContent(model), "model");

        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = form };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken);
        string raw = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Transcription failed ({(int)response.StatusCode}): {raw}");

        using JsonDocument doc = JsonDocument.Parse(raw);
        return doc.RootElement.TryGetProperty("text", out JsonElement text)
            ? text.GetString() ?? string.Empty
            : string.Empty;
    }
}
