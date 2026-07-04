namespace RavenAI.Services.Voice;

/// <summary>Selects which audio input the Azure Speech recognizer listens to.</summary>
public enum AudioInputSource
{
    /// <summary>The default recording device (microphone).</summary>
    Microphone,

    /// <summary>System playback via WASAPI loopback ("what you hear").</summary>
    SystemAudio,
}
