using System.Speech.Synthesis;

namespace RavenAI.Services.Voice;

/// <summary>
/// Windows built-in, fully offline text-to-speech using System.Speech (SAPI). Used as a
/// fallback when no network / provider TTS is desired. No API key or network required.
/// </summary>
public sealed class OfflineTextToSpeech : ITextToSpeech, IDisposable
{
    private readonly SpeechSynthesizer _synth = new();

    public OfflineTextToSpeech()
    {
        _synth.SetOutputToDefaultAudioDevice();
    }

    public Task SpeakAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return Task.CompletedTask;

        // SpeakAsync here means "off the UI thread"; SAPI itself is synchronous.
        return Task.Run(() =>
        {
            cancellationToken.Register(() => { try { _synth.SpeakAsyncCancelAll(); } catch { } });
            _synth.Speak(text);
        }, cancellationToken);
    }

    public void Dispose() => _synth.Dispose();
}
