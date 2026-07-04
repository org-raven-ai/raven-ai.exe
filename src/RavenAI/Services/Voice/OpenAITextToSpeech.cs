using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using NAudio.Wave;

namespace RavenAI.Services.Voice;

/// <summary>
/// Text-to-speech via an OpenAI-compatible /audio/speech endpoint. The returned audio
/// (MP3) is decoded and played back through NAudio.
/// </summary>
public sealed class OpenAITextToSpeech : ITextToSpeech
{
    private readonly HttpClient _http;
    private readonly Func<(string apiKey, string baseURL, string model, string voice)> _configProvider;

    public OpenAITextToSpeech(
        HttpClient http, Func<(string apiKey, string baseURL, string model, string voice)> configProvider)
    {
        _http = http;
        _configProvider = configProvider;
    }

    public async Task SpeakAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        var (apiKey, baseURL, model, voice) = _configProvider();
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("No API key configured.");

        string url = $"{baseURL.TrimEnd('/')}/audio/speech";
        var payload = new { model, voice, input = text, response_format = "mp3" };

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            string err = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"TTS failed ({(int)response.StatusCode}): {err}");
        }

        byte[] mp3 = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        await PlayMp3Async(mp3, cancellationToken);
    }

    private static async Task PlayMp3Async(byte[] mp3, CancellationToken cancellationToken)
    {
        using var ms = new MemoryStream(mp3);
        using var mp3Reader = new Mp3FileReader(ms);
        using var output = new WaveOutEvent();
        output.Init(mp3Reader);
        output.Play();

        // Wait for playback to finish (or cancellation) without blocking the thread.
        while (output.PlaybackState == PlaybackState.Playing)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                output.Stop();
                break;
            }
            await Task.Delay(100, CancellationToken.None);
        }
    }
}
