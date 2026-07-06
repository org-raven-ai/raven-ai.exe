namespace RavenAI.Models;

/// <summary>
/// One completed transcription line waiting in the staging window to be sent to the agent. It is
/// created when a channel's recognizer commits a final phrase (an interim "live" line becoming a
/// settled message), and it is discarded once the batch is sent to the chat or cleared.
/// </summary>
public sealed class StagedTranscript
{
    /// <summary>True for the microphone channel ("You", right side); false for system audio ("Interviewer", left side).</summary>
    public bool IsMic { get; }

    /// <summary>Short speaker label used both in the UI and in the message sent to the LLM (e.g. "You", "Interviewer").</summary>
    public string Speaker { get; }

    /// <summary>The committed transcription text.</summary>
    public string Text { get; }

    /// <summary>When the phrase was completed (its end time).</summary>
    public DateTime EndTime { get; }

    /// <summary>End time formatted for the timestamp shown under the message.</summary>
    public string TimeText => EndTime.ToString("HH:mm:ss");

    public StagedTranscript(bool isMic, string speaker, string text, DateTime endTime)
    {
        IsMic = isMic;
        Speaker = speaker;
        Text = text;
        EndTime = endTime;
    }
}
