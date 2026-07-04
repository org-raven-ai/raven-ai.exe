using System.Text;
using System.Text.Json;

namespace RavenAI.Services.Chat.Tools;

/// <summary>
/// Exposes the app's live speech-to-text transcript — the two Azure Speech channels ("You", the
/// microphone, and "Interviewer", the system audio) — so the model can pull in what was just said
/// and ground its answer in the ongoing conversation. Reads a fresh snapshot on each call.
/// </summary>
public sealed class TranscriptTool : IChatTool
{
    private readonly Func<(string you, string interviewer)> _transcriptProvider;

    public TranscriptTool(Func<(string you, string interviewer)> transcriptProvider)
    {
        _transcriptProvider = transcriptProvider;
    }

    public string Name => "get_live_transcript";

    public string Description =>
        "Get the current live speech-to-text transcript captured by the app: what 'You' (the microphone) " +
        "and the 'Interviewer' (system audio) have said so far. Use to ground answers in the ongoing conversation.";

    public BinaryData ParametersSchema => BinaryData.FromString(
        """{ "type": "object", "properties": {}, "additionalProperties": false }""");

    public Task<string> InvokeAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        var (you, interviewer) = _transcriptProvider();
        you = (you ?? string.Empty).Trim();
        interviewer = (interviewer ?? string.Empty).Trim();

        if (you.Length == 0 && interviewer.Length == 0)
            return Task.FromResult("The live transcript is currently empty (no speech captured yet).");

        var sb = new StringBuilder();
        sb.AppendLine("=== You (microphone) ===");
        sb.AppendLine(you.Length > 0 ? you : "(nothing captured)");
        sb.AppendLine();
        sb.AppendLine("=== Interviewer (system audio) ===");
        sb.AppendLine(interviewer.Length > 0 ? interviewer : "(nothing captured)");
        return Task.FromResult(sb.ToString().Trim());
    }
}
