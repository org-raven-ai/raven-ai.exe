namespace RavenAI.Services.Voice;

/// <summary>Speaks the given text aloud. Implementations block until playback finishes.</summary>
public interface ITextToSpeech
{
    Task SpeakAsync(string text, CancellationToken cancellationToken = default);
}
