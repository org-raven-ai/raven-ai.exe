using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace RavenAI.Services.Chat;

/// <summary>
/// Lists the model ids offered by an OpenAI-compatible /models endpoint. Used to populate the
/// model dropdowns in Settings. The API key and base URL are read fresh per call via the supplied
/// config provider so the newest saved credentials (and provider) are always used.
/// </summary>
public sealed class OpenAIModelCatalog
{
    private readonly HttpClient _http;
    private readonly Func<(string apiKey, string baseURL)> _configProvider;

    public OpenAIModelCatalog(HttpClient http, Func<(string apiKey, string baseURL)> configProvider)
    {
        _http = http;
        _configProvider = configProvider;
    }

    /// <summary>
    /// Fetches the available model ids, sorted alphabetically. Throws on a missing key or a failed
    /// request so the caller can surface the reason to the user.
    /// </summary>
    public async Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken cancellationToken = default)
    {
        var (apiKey, baseURL) = _configProvider();
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("No API key configured. Save your key first, then refresh.");

        string url = $"{baseURL.TrimEnd('/')}/models";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            string err = await SafeReadError(response, cancellationToken);
            throw new HttpRequestException(
                $"Could not list models ({(int)response.StatusCode} {response.ReasonPhrase}): {err}");
        }

        string json = await response.Content.ReadAsStringAsync(cancellationToken);
        return ParseModelIds(json);
    }

    /// <summary>Pulls data[].id out of the /models response, tolerating a missing/odd shape.</summary>
    private static IReadOnlyList<string> ParseModelIds(string json)
    {
        var ids = new List<string>();
        using JsonDocument doc = JsonDocument.Parse(json);
        // Standard OpenAI shape: { "data": [ { "id": "..." }, ... ] }.
        if (doc.RootElement.TryGetProperty("data", out JsonElement data) &&
            data.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in data.EnumerateArray())
            {
                if (item.TryGetProperty("id", out JsonElement id) &&
                    id.ValueKind == JsonValueKind.String)
                {
                    string? value = id.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                        ids.Add(value);
                }
            }
        }
        ids.Sort(StringComparer.OrdinalIgnoreCase);
        return ids;
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
