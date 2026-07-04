using System.ClientModel;
using System.Runtime.CompilerServices;
using System.Text;
using OpenAI;
using OpenAI.Chat;
using RavenAI.Services.Chat.Tools;
using ChatMessage = RavenAI.Models.ChatMessage;
using ChatRole = RavenAI.Models.ChatRole;

namespace RavenAI.Services.Chat;

/// <summary>
/// Streaming chat against any OpenAI-compatible /chat/completions endpoint, built on the official
/// OpenAI .NET SDK (<see cref="ChatClient"/>). A configurable base URL keeps this working against
/// OpenAI, Azure, Ollama, LM Studio, OpenRouter, etc.
///
/// When a <see cref="ChatToolRegistry"/> is supplied, the model can call tools: this runs the
/// agentic loop (stream → if the model requested tool calls, execute them and feed the results
/// back → stream again) until the model produces a final answer. The public contract is unchanged —
/// callers still receive a stream of assistant text deltas; tool call/result messages live only for
/// the duration of the request and are not surfaced to the UI (they're logged for visibility).
/// </summary>
public sealed class OpenAIChatProvider : IChatProvider
{
    /// <summary>Safety cap so a misbehaving model can't loop on tool calls forever.</summary>
    private const int MaxToolRounds = 6;

    private readonly Func<(string apiKey, string baseURL, string model, string systemPrompt)> _configProvider;
    private readonly ChatToolRegistry? _tools;

    /// <param name="configProvider">
    /// Called at request time so the freshest key/model are used. The key lives only for the
    /// duration of the call and is never stored on this object.
    /// </param>
    /// <param name="tools">Optional tool registry. When present, the model may call these tools.</param>
    public OpenAIChatProvider(
        Func<(string apiKey, string baseURL, string model, string systemPrompt)> configProvider,
        ChatToolRegistry? tools = null)
    {
        _configProvider = configProvider;
        _tools = tools;
    }

    public async IAsyncEnumerable<string> StreamAsync(
        IReadOnlyList<ChatMessage> conversation,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var (apiKey, baseURL, model, systemPrompt) = _configProvider();

        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("No API key configured. Open Settings and paste your key.");

        ChatClient client = CreateClient(apiKey, baseURL, model);

        // Translate the app's conversation into SDK messages. This list also accumulates the
        // assistant/tool messages produced across tool-call rounds within this single request.
        var messages = new List<OpenAI.Chat.ChatMessage>();
        if (!string.IsNullOrWhiteSpace(systemPrompt))
            messages.Add(new SystemChatMessage(systemPrompt));
        foreach (ChatMessage m in conversation)
        {
            messages.Add(m.Role switch
            {
                ChatRole.User => new UserChatMessage(m.Content),
                ChatRole.Assistant => new AssistantChatMessage(m.Content),
                _ => new SystemChatMessage(m.Content),
            });
        }

        bool hasTools = _tools is { HasTools: true };
        var options = new ChatCompletionOptions();
        if (hasTools)
        {
            foreach (ChatTool tool in _tools!.ToChatTools())
                options.Tools.Add(tool);
        }

        int round = 0;
        bool requiresAction;
        do
        {
            requiresAction = false;
            var contentBuilder = new StringBuilder();
            var toolCalls = new ToolCallAccumulator();
            ChatFinishReason? finishReason = null;

            AsyncCollectionResult<StreamingChatCompletionUpdate> updates =
                client.CompleteChatStreamingAsync(messages, options, cancellationToken);

            await foreach (StreamingChatCompletionUpdate update in updates.WithCancellation(cancellationToken))
            {
                foreach (ChatMessageContentPart part in update.ContentUpdate)
                {
                    if (!string.IsNullOrEmpty(part.Text))
                    {
                        contentBuilder.Append(part.Text);
                        yield return part.Text;
                    }
                }

                foreach (StreamingChatToolCallUpdate toolCallUpdate in update.ToolCallUpdates)
                    toolCalls.Append(toolCallUpdate);

                if (update.FinishReason is not null)
                    finishReason = update.FinishReason;
            }

            if (hasTools && finishReason == ChatFinishReason.ToolCalls)
            {
                IReadOnlyList<ChatToolCall> calls = toolCalls.Build();
                if (calls.Count == 0)
                    break; // nothing actionable; avoid an empty follow-up round

                // Record the assistant turn (its tool calls, plus any text it emitted first).
                var assistantMessage = new AssistantChatMessage(calls);
                if (contentBuilder.Length > 0)
                    assistantMessage.Content.Add(ChatMessageContentPart.CreateTextPart(contentBuilder.ToString()));
                messages.Add(assistantMessage);

                // Execute each tool and append its result as a tool message.
                foreach (ChatToolCall call in calls)
                {
                    string argsJson = call.FunctionArguments is { } args ? args.ToString() : "{}";
                    string result = await _tools!.InvokeAsync(call.FunctionName, argsJson, cancellationToken);
                    messages.Add(new ToolChatMessage(call.Id, result));
                }

                requiresAction = true;
            }
        }
        while (requiresAction && ++round < MaxToolRounds);
    }

    private static ChatClient CreateClient(string apiKey, string baseURL, string model)
    {
        var options = new OpenAIClientOptions
        {
            // Match the previous HttpClient timeout so long streaming replies aren't cut off.
            NetworkTimeout = TimeSpan.FromMinutes(5),
        };
        if (!string.IsNullOrWhiteSpace(baseURL))
            options.Endpoint = new Uri(baseURL);

        return new ChatClient(model, new ApiKeyCredential(apiKey), options);
    }

    /// <summary>
    /// Reassembles streamed tool-call deltas into complete <see cref="ChatToolCall"/>s. Each delta
    /// carries an index; the id and function name arrive on the first delta for that index, and the
    /// JSON arguments arrive as fragments to be concatenated.
    /// </summary>
    private sealed class ToolCallAccumulator
    {
        private readonly Dictionary<int, Entry> _byIndex = new();

        private sealed class Entry
        {
            public string? Id;
            public string? Name;
            public readonly StringBuilder Arguments = new();
        }

        public void Append(StreamingChatToolCallUpdate update)
        {
            if (!_byIndex.TryGetValue(update.Index, out Entry? entry))
            {
                entry = new Entry();
                _byIndex[update.Index] = entry;
            }

            if (!string.IsNullOrEmpty(update.ToolCallId))
                entry.Id = update.ToolCallId;
            if (!string.IsNullOrEmpty(update.FunctionName))
                entry.Name = update.FunctionName;
            if (update.FunctionArgumentsUpdate is { } fragment)
                entry.Arguments.Append(fragment.ToString());
        }

        public IReadOnlyList<ChatToolCall> Build()
        {
            var calls = new List<ChatToolCall>(_byIndex.Count);
            foreach (KeyValuePair<int, Entry> kvp in _byIndex.OrderBy(k => k.Key))
            {
                Entry e = kvp.Value;
                if (string.IsNullOrEmpty(e.Id) || string.IsNullOrEmpty(e.Name))
                    continue;
                string args = e.Arguments.Length == 0 ? "{}" : e.Arguments.ToString();
                calls.Add(ChatToolCall.CreateFunctionToolCall(e.Id, e.Name, BinaryData.FromString(args)));
            }
            return calls;
        }
    }
}
