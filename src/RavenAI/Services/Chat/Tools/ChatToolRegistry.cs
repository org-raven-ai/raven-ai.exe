using System.Text.Json;
using OpenAI.Chat;
using RavenAI.Services.Logging;

namespace RavenAI.Services.Chat.Tools;

/// <summary>
/// Holds the set of tools the assistant may call, builds their OpenAI <see cref="ChatTool"/>
/// definitions for a request, and dispatches a model tool-call to the matching implementation.
/// Adding a new capability is just registering another <see cref="IChatTool"/> here.
/// </summary>
public sealed class ChatToolRegistry
{
    private readonly Dictionary<string, IChatTool> _tools;

    public ChatToolRegistry(IEnumerable<IChatTool> tools)
    {
        _tools = tools.ToDictionary(t => t.Name, StringComparer.Ordinal);
    }

    /// <summary>True when at least one tool is registered (so the provider only attaches tools when useful).</summary>
    public bool HasTools => _tools.Count > 0;

    /// <summary>OpenAI tool definitions to attach to <see cref="ChatCompletionOptions.Tools"/>.</summary>
    public IEnumerable<ChatTool> ToChatTools() =>
        _tools.Values.Select(t => ChatTool.CreateFunctionTool(t.Name, t.Description, t.ParametersSchema));

    /// <summary>
    /// Runs the named tool with the model's raw JSON argument string and returns its textual result.
    /// Never throws (except on cancellation): an unknown tool, malformed JSON, or a failing tool
    /// becomes an error string handed back to the model so it can recover, rather than tearing down
    /// the stream.
    /// </summary>
    public async Task<string> InvokeAsync(string name, string argumentsJson, CancellationToken cancellationToken)
    {
        if (!_tools.TryGetValue(name, out IChatTool? tool))
            return $"Error: no tool named '{name}' is registered.";

        JsonElement args;
        try
        {
            using JsonDocument doc = JsonDocument.Parse(
                string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
            args = doc.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            return $"Error: the arguments for '{name}' were not valid JSON ({ex.Message}).";
        }

        try
        {
            Log.Info($"Tool call: {name}({argumentsJson})", "Tools");
            string result = await tool.InvokeAsync(args, cancellationToken);
            Log.Info($"Tool result: {name} -> {Trim(result)}", "Tools");
            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Warning($"Tool '{name}' failed", ex, "Tools");
            return $"Error running '{name}': {ex.Message}";
        }
    }

    private static string Trim(string s) => s.Length <= 200 ? s : s[..200] + "…";
}
