namespace RavenAI.Services.Voice;

/// <summary>Transcribes recorded audio (WAV bytes) into text.</summary>
public interface ISpeechToText
{
    Task<string> TranscribeAsync(byte[] wavAudio, CancellationToken cancellationToken = default);
}
