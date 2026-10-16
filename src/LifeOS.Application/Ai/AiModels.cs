using LifeOS.Domain.Ai;
using LifeOS.Domain.Goals;

namespace LifeOS.Application.Ai;

public sealed class AiOptions
{
    public const string SectionName = "LifeOS:Ai";

    /// <summary>Most AI calls per owner and (local) day.</summary>
    public int DailyLimit { get; set; } = 30;

    /// <summary>A dashboard insight is reused for this long while its data is unchanged.</summary>
    public int InsightTtlHours { get; set; } = 12;

    /// <summary>Upper bound for the context sent with a request (characters). Anything beyond is cut.</summary>
    public int MaxContextChars { get; set; } = 8000;
}

public enum AiStatus
{
    Ok,

    /// <summary>No provider/key configured on this server.</summary>
    NotConfigured,

    /// <summary>The owner has not turned AI on.</summary>
    Disabled,

    /// <summary>The daily limit or the provider's limit was reached.</summary>
    RateLimited,

    /// <summary>The provider failed or answered unusably. Nothing was changed.</summary>
    Failed,
}

public sealed record AiResult<T>(AiStatus Status, T? Value = default)
{
    public bool Ok => Status == AiStatus.Ok;

    public static AiResult<T> Success(T value) => new(AiStatus.Ok, value);

    public static AiResult<T> Fail(AiStatus status) => new(status);
}

public sealed record AiAvailability(bool ProviderConfigured, bool EnabledByOwner, int CallsToday, int DailyLimit)
{
    public bool CanUse => ProviderConfigured && EnabledByOwner && CallsToday < DailyLimit;
}

public sealed record AiPreferenceView(bool Enabled, AiCoachMode Mode);

/// <summary>A coach reply: what the data shows, tentative interpretations, and one question. The first two are kept apart on purpose.</summary>
public sealed record CoachReply(IReadOnlyList<string> Observations, IReadOnlyList<string> Hypotheses, string Question);

public sealed record GoalSuggestion(
    string Title,
    string? Description,
    GoalHorizon Horizon,
    GoalProgressMode Mode,
    decimal? Start,
    decimal? Target,
    string? Unit,
    IReadOnlyList<string> Milestones);

public sealed record DashboardInsight(
    IReadOnlyList<string> Observations,
    IReadOnlyList<string> Hypotheses,
    DateTimeOffset GeneratedUtc,
    bool FromCache);

public sealed record ProposalView(
    Guid Id,
    AiProposalKind Kind,
    AiProposalStatus Status,
    string Summary,
    string? Rationale,
    string Context,
    string? LastError);

public sealed record RetroChatMessage(
    Guid Id,
    bool IsUser,
    string Content,
    AiCoachMode Mode,
    IReadOnlyList<string> Observations,
    IReadOnlyList<string> Hypotheses,
    DateTimeOffset CreatedUtc);

/// <summary>Context keys for proposals, so they can be listed where they belong.</summary>
public static class ProposalContext
{
    public static string Sprint(DateOnly weekStart) => $"sprint:{weekStart:yyyy-MM-dd}";

    public static string Retro(Guid retroId) => $"retro:{retroId}";
}

public interface IAiCoachService
{
    Task<AiAvailability> GetAvailabilityAsync(CancellationToken ct = default);

    /// <summary>The coach reads the retro so far and asks a follow-up question. Stores the exchange with the retro.</summary>
    Task<AiResult<CoachReply>> RetroFollowUpAsync(Guid retroId, AiCoachMode? mode, string? ownerMessage, CancellationToken ct = default);

    /// <summary>Suggests actions/experiments. They become Pending proposals; nothing is created until the owner accepts.</summary>
    Task<AiResult<IReadOnlyList<Guid>>> ProposeRetroActionsAsync(Guid retroId, AiCoachMode? mode, CancellationToken ct = default);

    /// <summary>Turns a rough idea into a structured goal suggestion. Pre-fills a form; saves nothing.</summary>
    Task<AiResult<GoalSuggestion>> FormulateGoalAsync(string roughIdea, GoalHorizon horizon, CancellationToken ct = default);

    /// <summary>Suggests which backlog tasks could fit the week. They become Pending proposals.</summary>
    Task<AiResult<IReadOnlyList<Guid>>> SuggestSprintAsync(DateOnly anyDateInWeek, CancellationToken ct = default);

    /// <summary>On-demand dashboard insight, cached and rate limited. Never called when a page renders.</summary>
    Task<AiResult<DashboardInsight>> GenerateInsightAsync(bool force, CancellationToken ct = default);

    /// <summary>The stored coach conversation for a retro, oldest first.</summary>
    Task<IReadOnlyList<RetroChatMessage>> GetRetroChatAsync(Guid retroId, CancellationToken ct = default);

    /// <summary>The latest cached insight without calling the provider.</summary>
    Task<DashboardInsight?> GetCachedInsightAsync(CancellationToken ct = default);
}
