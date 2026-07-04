using System.ClientModel;
using System.IO;
using NAudio.Wave;
using OpenAI;
using OpenAI.Audio;

namespace RavenAI.Services.Voice;

/// <summary>
/// Text-to-speech via an OpenAI-compatible /audio/speech endpoint, using the official OpenAI .NET
/// SDK (<see cref="AudioClient"/>). The returned MP3 is decoded and played back through NAudio.
/// </summary>
public sealed class OpenAITextToSpeech : ITextToSpeech
{
    private readonly Func<(string apiKey, string baseURL, string model, string voice)> _configProvider;

    public OpenAITextToSpeech(Func<(string apiKey, string baseURL, string model, string voice)> configProvider)
    {
        _configProvider = configProvider;
    }

    public async Task SpeakAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        var (apiKey, baseURL, model, voice) = _configProvider();
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("No API key configured.");

        AudioClient client = CreateClient(apiKey, baseURL, model);

        BinaryData speech = await client.GenerateSpeechAsync(text, new GeneratedSpeechVoice(voice));
        await PlayMp3Async(speech.ToArray(), cancellationToken);
    }

    private static AudioClient CreateClient(string apiKey, string baseURL, string model)
    {
        var options = new OpenAIClientOptions();
        if (!string.IsNullOrWhiteSpace(baseURL))
            options.Endpoint = new Uri(baseURL);
        return new AudioClient(model, new ApiKeyCredential(apiKey), options);
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
