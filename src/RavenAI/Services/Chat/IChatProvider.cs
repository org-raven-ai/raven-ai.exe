using RavenAI.Models;

namespace RavenAI.Services.Chat;

/// <summary>Abstraction over a streaming chat backend.</summary>
public interface IChatProvider
{
    /// <summary>
    /// Streams the assistant reply as a sequence of token deltas (text fragments) for the
    /// given conversation. Implementations should honour <paramref name="cancellationToken"/>.
    /// </summary>
    IAsyncEnumerable<string> StreamAsync(
        IReadOnlyList<ChatMessage> conversation,
        CancellationToken cancellationToken = default);
}
