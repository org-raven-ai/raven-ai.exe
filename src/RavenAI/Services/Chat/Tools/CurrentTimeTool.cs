using System.Text.Json;

namespace RavenAI.Services.Chat.Tools;

/// <summary>
/// Returns the current local date/time (and UTC) from the system clock. Local, no network — a good
/// smoke test that the whole tool-call loop works end to end.
/// </summary>
public sealed class CurrentTimeTool : IChatTool
{
    public string Name => "get_current_time";

    public string Description =>
        "Get the current local date and time (and the UTC equivalent). Use whenever the user asks " +
        "what time or date it is, or when you need the current moment to reason about scheduling.";

    public BinaryData ParametersSchema => BinaryData.FromString(
        """{ "type": "object", "properties": {}, "additionalProperties": false }""");

    public Task<string> InvokeAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        DateTimeOffset now = DateTimeOffset.Now;
        string result =
            $"Local: {now:dddd, yyyy-MM-dd HH:mm:ss} ({TimeZoneInfo.Local.StandardName}); " +
            $"UTC: {now.ToUniversalTime():yyyy-MM-dd HH:mm:ss}Z";
        return Task.FromResult(result);
    }
}
