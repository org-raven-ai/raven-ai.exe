using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace RavenAI.Services.Chat.Tools;

/// <summary>
/// Web search via the Tavily API (https://tavily.com) — a search endpoint that returns concise,
/// LLM-ready results. Requires a Tavily API key configured in Settings; without one the tool
/// returns a clear "not configured" message so the model can relay that to the user rather than
/// failing the turn. The key and endpoint are read fresh per call.
/// </summary>
public sealed class WebSearchTool : IChatTool
{
    private readonly HttpClient _http;
    private readonly Func<(string apiKey, string endpoint)> _configProvider;

    public WebSearchTool(HttpClient http, Func<(string apiKey, string endpoint)> configProvider)
    {
        _http = http;
        _configProvider = configProvider;
    }

    public string Name => "web_search";

    public string Description =>
        "Search the web for current or factual information you may not know (news, recent events, " +
        "documentation, specific facts). Returns a short synthesized answer plus result titles, URLs, and snippets.";

    public BinaryData ParametersSchema => BinaryData.FromString(
        """
        {
          "type": "object",
          "properties": {
            "query": { "type": "string", "description": "The search query." },
            "max_results": { "type": "integer", "description": "How many results to return (1-10). Default 5." }
          },
          "required": ["query"],
          "additionalProperties": false
        }
        """);

    public async Task<string> InvokeAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        var (apiKey, endpoint) = _configProvider();
        if (string.IsNullOrWhiteSpace(apiKey))
            return "Web search is not configured. Ask the user to add a Tavily API key in Settings → Web Search.";

        if (!arguments.TryGetProperty("query", out JsonElement q) || q.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(q.GetString()))
            return "Error: missing required 'query' string argument.";

        int maxResults = 5;
        if (arguments.TryGetProperty("max_results", out JsonElement mr) && mr.ValueKind == JsonValueKind.Number
            && mr.TryGetInt32(out int m))
            maxResults = Math.Clamp(m, 1, 10);

        string url = string.IsNullOrWhiteSpace(endpoint) ? "https://api.tavily.com/search" : endpoint;

        var payload = new
        {
            api_key = apiKey,
            query = q.GetString(),
            max_results = maxResults,
            search_depth = "basic",
            include_answer = true,
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
        };

        using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken);
        string raw = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            return $"Web search failed ({(int)response.StatusCode} {response.ReasonPhrase}): {raw}";

        return FormatResults(raw);
    }

    /// <summary>Renders Tavily's { answer, results[] } payload into a compact, model-readable block.</summary>
    private static string FormatResults(string json)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            var sb = new StringBuilder();

            if (doc.RootElement.TryGetProperty("answer", out JsonElement answer) &&
                answer.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(answer.GetString()))
            {
                sb.AppendLine($"Answer: {answer.GetString()}");
                sb.AppendLine();
            }

            if (doc.RootElement.TryGetProperty("results", out JsonElement results) &&
                results.ValueKind == JsonValueKind.Array)
            {
                int i = 1;
                foreach (JsonElement r in results.EnumerateArray())
                {
                    string title = r.TryGetProperty("title", out var t) ? t.GetString() ?? string.Empty : string.Empty;
                    string rurl = r.TryGetProperty("url", out var u) ? u.GetString() ?? string.Empty : string.Empty;
                    string content = r.TryGetProperty("content", out var c) ? c.GetString() ?? string.Empty : string.Empty;
                    sb.AppendLine($"{i}. {title}");
                    sb.AppendLine($"   {rurl}");
                    sb.AppendLine($"   {Truncate(content, 300)}");
                    i++;
                }
            }

            string result = sb.ToString().Trim();
            return result.Length == 0 ? "No results found." : result;
        }
        catch (JsonException)
        {
            return json; // hand the raw payload back; the model can still use it
        }
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}
