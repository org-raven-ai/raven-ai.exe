using CommunityToolkit.Mvvm.ComponentModel;
using RavenAI.Services.Voice;

namespace RavenAI.ViewModels;

/// <summary>
/// Backs the Speech-to-Text panel. Runs two independent live-transcription channels side by side:
/// <see cref="You"/> transcribes the interviewee's microphone, and <see cref="Interviewer"/>
/// transcribes system audio ("what you hear") — the interviewer's voice coming out of your
/// speakers. Each channel owns its own recognizer, controls, and transcript, so they can be
/// started, stopped, and read independently.
/// </summary>
public sealed partial class SpeechViewModel : ObservableObject, IDisposable
{
    /// <summary>The interviewee's microphone channel.</summary>
    public SpeechChannelViewModel You { get; }

    /// <summary>The interviewer's system-audio (loopback) channel.</summary>
    public SpeechChannelViewModel Interviewer { get; }

    public SpeechViewModel(AzureSpeechRecognizer microphoneRecognizer, AzureSpeechRecognizer systemAudioRecognizer)
    {
        You = new SpeechChannelViewModel("You (microphone)", microphoneRecognizer, AudioInputSource.Microphone);
        Interviewer = new SpeechChannelViewModel("Interviewer (system audio)", systemAudioRecognizer, AudioInputSource.SystemAudio);
    }

    public void Dispose()
    {
        You.Dispose();
        Interviewer.Dispose();
    }
}
