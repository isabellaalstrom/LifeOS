namespace LifeOS.Application.Abstractions;

public sealed record AiChatMessage(string Role, string Content);

public sealed record AiRequest(
    string System,
    IReadOnlyList<AiChatMessage> Messages,
    bool JsonOutput = true,
    int MaxOutputTokens = 900);

public enum AiFailure
{
    None,

    /// <summary>No API key (or provider) configured.</summary>
    NotConfigured,

    /// <summary>The provider asked us to slow down.</summary>
    RateLimited,

    /// <summary>The key was rejected.</summary>
    Rejected,

    /// <summary>Network error, timeout or a server error at the provider.</summary>
    Unavailable,

    /// <summary>The provider answered but not in a usable way.</summary>
    BadResponse,
}

public sealed record AiCompletion(string? Text, AiFailure Failure)
{
    public bool Ok => Failure == AiFailure.None && Text is not null;

    public static AiCompletion Success(string text) => new(text, AiFailure.None);

    public static AiCompletion Failed(AiFailure failure) => new(null, failure);
}

/// <summary>A language-model provider (OpenAI first). Implemented in Infrastructure; the app works without one.</summary>
public interface IAiClient
{
    bool IsConfigured { get; }

    Task<AiCompletion> CompleteAsync(AiRequest request, CancellationToken cancellationToken = default);
}
