using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RavenAI.Models;
using RavenAI.Services.Logging;
using RavenAI.Services.Voice;

namespace RavenAI.ViewModels;

/// <summary>
/// Backs the transcription-staging window. Runs two independent live-transcription channels side by
/// side: <see cref="You"/> transcribes the interviewee's microphone (right), and
/// <see cref="Interviewer"/> transcribes system audio ("what you hear") — the interviewer's voice
/// coming out of your speakers (left). Each channel owns its own recognizer and controls.
///
/// As each channel commits a final phrase it is appended to <see cref="Staged"/> as a settled
/// message with a completion timestamp. Pressing Send hands the whole staged batch to the agent
/// chat (via <see cref="SendToChat"/>) as one chronological, speaker-labeled message and clears the
/// staging area.
/// </summary>
public sealed partial class SpeechViewModel : ObservableObject, IDisposable
{
    /// <summary>The interviewee's microphone channel (right side).</summary>
    public SpeechChannelViewModel You { get; }

    /// <summary>The interviewer's system-audio (loopback) channel (left side).</summary>
    public SpeechChannelViewModel Interviewer { get; }

    /// <summary>Completed transcriptions waiting to be sent, in completion order (both channels interleaved).</summary>
    public ObservableCollection<StagedTranscript> Staged { get; } = new();

    /// <summary>
    /// Hands a batch of staged text to the agent chat. Wired by <see cref="MainViewModel"/> to the
    /// chat's external-submit path. Returns once the message has been submitted (streaming may
    /// still be in flight).
    /// </summary>
    public Func<string, Task>? SendToChat { get; set; }

    public SpeechViewModel(AzureSpeechRecognizer microphoneRecognizer, AzureSpeechRecognizer systemAudioRecognizer)
    {
        You = new SpeechChannelViewModel("You (microphone)", microphoneRecognizer, AudioInputSource.Microphone);
        Interviewer = new SpeechChannelViewModel("Interviewer (system audio)", systemAudioRecognizer, AudioInputSource.SystemAudio);

        // Each committed phrase becomes a staged message. FinalCommitted is raised on the UI thread.
        You.FinalCommitted += text => Stage(You, text);
        Interviewer.FinalCommitted += text => Stage(Interviewer, text);

        // Keep the Send button's enabled state in sync with whether anything is staged.
        Staged.CollectionChanged += OnStagedChanged;
    }

    /// <summary>True when at least one message is staged and ready to send.</summary>
    public bool HasStaged => Staged.Count > 0;

    private void Stage(SpeechChannelViewModel channel, string text)
    {
        // "You" for the mic, "Interviewer" for system audio — short labels reused verbatim in the
        // message sent to the LLM.
        string speaker = channel.IsMicrophone ? "You" : "Interviewer";
        Staged.Add(new StagedTranscript(channel.IsMicrophone, speaker, text, DateTime.Now));
    }

    private void OnStagedChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasStaged));
        SendCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Sends every staged message to the agent chat as one chronological, speaker-labeled user
    /// message, then clears the staging area. The staging area is cleared up front so it empties
    /// immediately, before the (possibly slow) streaming reply completes.
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasStaged), AllowConcurrentExecutions = false)]
    private async Task SendAsync()
    {
        if (Staged.Count == 0 || SendToChat is null) return;

        var sb = new StringBuilder();
        foreach (var message in Staged)
            sb.AppendLine($"{message.Speaker}: {message.Text}");

        string combined = sb.ToString().TrimEnd();
        Staged.Clear();

        try { await SendToChat(combined); }
        catch (Exception ex) { Log.Error("Failed to send staged transcript to chat", ex, "Speech"); }
    }

    /// <summary>Discards the staged messages without sending them.</summary>
    [RelayCommand]
    private void ClearStaged() => Staged.Clear();

    public void Dispose()
    {
        You.Dispose();
        Interviewer.Dispose();
    }
}
