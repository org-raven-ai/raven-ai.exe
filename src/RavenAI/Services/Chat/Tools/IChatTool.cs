using System.Text.Json;

namespace RavenAI.Services.Chat.Tools;

/// <summary>
/// A single function the chat model may invoke during a completion. Each tool advertises a stable
/// name, a description (the model reads this to decide when to call it), and a JSON Schema for its
/// parameters, and knows how to execute a call given the model-supplied arguments.
///
/// Implementations must be defensive: the model can omit required arguments or hallucinate values,
/// so validate before acting and return an explanatory string on bad input rather than throwing.
/// </summary>
public interface IChatTool
{
    /// <summary>Function name exposed to the model (snake_case, stable — the model calls by this).</summary>
    string Name { get; }

    /// <summary>What the tool does and when to use it. The model relies on this to choose the tool.</summary>
    string Description { get; }

    /// <summary>JSON Schema describing the tool's parameters object (an OpenAI function-tool schema).</summary>
    BinaryData ParametersSchema { get; }

    /// <summary>
    /// Executes the tool. <paramref name="arguments"/> is the already-parsed JSON arguments object
    /// the model supplied. Returns the textual result fed back to the model as the tool message.
    /// </summary>
    Task<string> InvokeAsync(JsonElement arguments, CancellationToken cancellationToken);
}
