using LifeOS.Domain.Common;

namespace LifeOS.Domain.Ai;

public enum AiCoachMode
{
    /// <summary>Warm and practical with a bit of both.</summary>
    Balanced,

    /// <summary>Focuses on the numbers and patterns.</summary>
    Analytical,

    /// <summary>Focuses on feelings, meaning and gentle questions.</summary>
    Reflective,
}

public enum AiFeature
{
    RetroCoach,
    GoalFormulation,
    SprintSuggestions,
    DashboardInsight,
}

public enum AiProposalStatus
{
    Pending,
    Accepted,
    Rejected,
}

public enum AiProposalKind
{
    /// <summary>Plan an existing backlog task into a week's sprint.</summary>
    PlanTask,

    /// <summary>Add an action or experiment to a retro.</summary>
    CreateRetroAction,

    CreateGoal,
}

/// <summary>Per-owner AI settings. AI is off until the owner turns it on, and the app works fully without it.</summary>
public class UserPreference : OwnedEntity
{
    public bool AiEnabled { get; set; }

    public AiCoachMode AiMode { get; set; } = AiCoachMode.Balanced;

    /// <summary>Show Projects and Life areas in the navigation. Off keeps the app to goals, planning, habits and retros.</summary>
    public bool ShowProjectsAndAreas { get; set; }
}

/// <summary>
/// Something the AI suggests. The AI never changes data itself: a proposal waits as Pending until the owner accepts it, and
/// accepting goes through the normal application services, which validate it again.
/// </summary>
public class AiProposal : OwnedEntity
{
    public AiProposalKind Kind { get; set; }

    public AiProposalStatus Status { get; set; } = AiProposalStatus.Pending;

    /// <summary>One line the owner reads before deciding.</summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>The AI's reasoning. Shown as the AI's view, not as fact.</summary>
    public string? Rationale { get; set; }

    /// <summary>Validated, typed data needed to apply the proposal (JSON).</summary>
    public string PayloadJson { get; set; } = "{}";

    /// <summary>What it belongs to, e.g. "sprint:2026-10-12" or "retro:{id}". Used to list proposals in context.</summary>
    public string Context { get; set; } = string.Empty;

    public DateTimeOffset? DecidedUtc { get; set; }

    public Guid? AppliedEntityId { get; set; }

    /// <summary>Why the last attempt to apply it failed (it stays Pending so the owner can see and dismiss it).</summary>
    public string? LastError { get; set; }
}

/// <summary>A generated dashboard insight, cached so it is never regenerated on a page render.</summary>
public class AiInsight : OwnedEntity
{
    public AiFeature Feature { get; set; }

    /// <summary>Hash of the data the insight was based on; the cache is reused only while this is unchanged and fresh.</summary>
    public string ContextHash { get; set; } = string.Empty;

    /// <summary>What the data shows (newline separated).</summary>
    public string Observations { get; set; } = string.Empty;

    /// <summary>Tentative interpretations, clearly separate from observations (newline separated).</summary>
    public string Hypotheses { get; set; } = string.Empty;

    public DateTimeOffset GeneratedUtc { get; set; }
}

/// <summary>One row per attempted AI call, used for rate limiting and transparency. Contains no content.</summary>
public class AiCallLog : OwnedEntity
{
    public AiFeature Feature { get; set; }

    public DateTimeOffset OccurredUtc { get; set; }

    public bool Success { get; set; }

    public string Status { get; set; } = string.Empty;
}

/// <summary>A message in the AI-guided part of a retro.</summary>
public class AiRetroMessage : OwnedEntity
{
    public Guid RetroId { get; set; }

    public bool IsUser { get; set; }

    /// <summary>The owner's reply, or the coach's question/text.</summary>
    public string Content { get; set; } = string.Empty;

    public AiCoachMode Mode { get; set; }

    /// <summary>For coach messages: what the data shows (newline separated).</summary>
    public string? Observations { get; set; }

    /// <summary>For coach messages: tentative interpretations (newline separated).</summary>
    public string? Hypotheses { get; set; }
}
