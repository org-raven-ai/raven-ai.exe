using System.ClientModel;
using OpenAI;
using OpenAI.Models;

namespace RavenAI.Services.Chat;

/// <summary>
/// Lists the model ids offered by an OpenAI-compatible provider, using the official OpenAI .NET SDK
/// (<see cref="OpenAIModelClient"/>). Used to populate the model dropdowns in Settings. The API key
/// and base URL are read fresh per call so the newest saved credentials (and provider) are used.
/// </summary>
public sealed class OpenAIModelCatalog
{
    private readonly Func<(string apiKey, string baseURL)> _configProvider;

    public OpenAIModelCatalog(Func<(string apiKey, string baseURL)> configProvider)
    {
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

        OpenAIModelClient client = CreateClient(apiKey, baseURL);

        OpenAIModelCollection models = await client.GetModelsAsync(cancellationToken);
        var ids = models
            .Select(m => m.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToList();
        ids.Sort(StringComparer.OrdinalIgnoreCase);
        return ids;
    }

    private static OpenAIModelClient CreateClient(string apiKey, string baseURL)
    {
        var options = new OpenAIClientOptions();
        if (!string.IsNullOrWhiteSpace(baseURL))
            options.Endpoint = new Uri(baseURL);
        return new OpenAIModelClient(new ApiKeyCredential(apiKey), options);
    }
}
