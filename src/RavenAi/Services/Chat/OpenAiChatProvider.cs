using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using RavenAi.Models;

namespace RavenAi.Services.Chat;

/// <summary>
/// Streaming chat against any OpenAI-compatible /chat/completions endpoint (configurable
/// base URL + model). Uses Server-Sent Events (stream=true) so tokens arrive incrementally.
/// </summary>
public sealed class OpenAiChatProvider : IChatProvider
{
    private readonly HttpClient _http;
    private readonly Func<(string apiKey, string baseUrl, string model, string systemPrompt)> _configProvider;

    /// <param name="configProvider">
    /// Called at request time so the freshest key/model are used. The key lives only for the
    /// duration of the call and is never stored on this object.
    /// </param>
    public OpenAiChatProvider(
        HttpClient http,
        Func<(string apiKey, string baseUrl, string model, string systemPrompt)> configProvider)
    {
        _http = http;
        _configProvider = configProvider;
    }

    public async IAsyncEnumerable<string> StreamAsync(
        IReadOnlyList<ChatMessage> conversation,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var (apiKey, baseUrl, model, systemPrompt) = _configProvider();

        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("No API key configured. Open Settings and paste your key.");

        string url = $"{baseUrl.TrimEnd('/')}/chat/completions";

        // Build the request body. A system prompt (if any) is prepended.
        var messages = new List<object>();
        if (!string.IsNullOrWhiteSpace(systemPrompt))
            messages.Add(new { role = "system", content = systemPrompt });
        foreach (var m in conversation)
        {
            string role = m.Role switch
            {
                ChatRole.User => "user",
                ChatRole.Assistant => "assistant",
                _ => "system",
            };
            messages.Add(new { role, content = m.Content });
        }

        var payload = new { model, messages, stream = true };
        string bodyJson = JsonSerializer.Serialize(payload);

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = new StringContent(bodyJson, Encoding.UTF8, "application/json");

        using HttpResponseMessage response = await _http.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            string err = await SafeReadError(response, cancellationToken);
            throw new HttpRequestException(
                $"Chat request failed ({(int)response.StatusCode} {response.ReasonPhrase}): {err}");
        }

        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        // Parse the SSE stream. Each event line looks like: "data: {json}" or "data: [DONE]".
        while (!reader.EndOfStream)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrEmpty(line)) continue;
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;

            string data = line["data:".Length..].Trim();
            if (data == "[DONE]") yield break;

            string? delta = ExtractDelta(data);
            if (!string.IsNullOrEmpty(delta))
                yield return delta;
        }
    }

    /// <summary>Pulls choices[0].delta.content out of one SSE JSON chunk, tolerating partial data.</summary>
    private static string? ExtractDelta(string json)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("choices", out JsonElement choices) ||
                choices.GetArrayLength() == 0)
                return null;

            JsonElement choice = choices[0];
            if (choice.TryGetProperty("delta", out JsonElement delta) &&
                delta.TryGetProperty("content", out JsonElement content) &&
                content.ValueKind == JsonValueKind.String)
                return content.GetString();

            return null;
        }
        catch (JsonException)
        {
            return null; // ignore keep-alive / malformed partial chunks
        }
    }

    private static async Task<string> SafeReadError(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            string raw = await response.Content.ReadAsStringAsync(ct);
            // Try to surface the provider's error.message if present.
            try
            {
                using JsonDocument doc = JsonDocument.Parse(raw);
                if (doc.RootElement.TryGetProperty("error", out JsonElement e) &&
                    e.TryGetProperty("message", out JsonElement m))
                    return m.GetString() ?? raw;
            }
            catch (JsonException) { /* not JSON */ }
            return string.IsNullOrWhiteSpace(raw) ? "(no response body)" : raw;
        }
        catch
        {
            return "(could not read error body)";
        }
    }
}
