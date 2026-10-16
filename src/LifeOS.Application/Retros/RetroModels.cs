using LifeOS.Domain.Retros;

namespace LifeOS.Application.Retros;

public sealed record RetroSprintStats(
    int SprintCount, int Planned, int AddedAfterStart, int Removed, int CarriedOut, int CarriedIn, int Completed);

public sealed record RetroGoalChange(Guid GoalId, string Title, decimal? StartPercent, decimal EndPercent, bool AchievedInPeriod);

public sealed record RetroHabitStat(Guid HabitId, string Name, int PeriodsMet, int PeriodsTotal, int Completions);

/// <summary>
/// Facts about a period, never a verdict. Missing data simply shows as zero or is left out: incomplete metrics
/// are not failures.
/// </summary>
public sealed record RetroStats(
    DateOnly PeriodStart,
    DateOnly PeriodEndExclusive,
    bool InProgress,
    int TasksCompleted,
    int TasksCompletedMinutes,
    RetroSprintStats Sprints,
    IReadOnlyList<RetroGoalChange> Goals,
    IReadOnlyList<RetroHabitStat> Habits);

public sealed record RetroView(
    Guid Id,
    RetroType Type,
    DateOnly PeriodStart,
    DateOnly PeriodEndExclusive,
    RetroStatus Status,
    DateTimeOffset? CompletedUtc,
    string? Summary,
    int? Energy,
    int? Balance,
    int? Stress,
    int? Satisfaction,
    int AnswerCount,
    DateTimeOffset UpdatedUtc);

public sealed record RetroActionView(
    Guid Id,
    Guid? RetroId,
    RetroActionKind Kind,
    string Title,
    string? Details,
    string? OwnerName,
    DateOnly? StartDate,
    DateOnly? DueDate,
    string? Hypothesis,
    RetroActionStatus Status,
    Guid? TaskItemId,
    IReadOnlyList<RetroFollowUpView> FollowUps);

public sealed record RetroFollowUpView(Guid Id, Guid? RetroId, DateTimeOffset RecordedUtc, RetroOutcome Outcome, string? Note);

public sealed record RetroActionInput(
    string Title,
    RetroActionKind Kind = RetroActionKind.Action,
    string? Details = null,
    string? OwnerName = null,
    DateOnly? StartDate = null,
    DateOnly? DueDate = null,
    string? Hypothesis = null);

public sealed record WellbeingInput(int? Energy, int? Balance, int? Stress, int? Satisfaction);

public sealed record RetroDetail(
    RetroView Retro,
    IReadOnlyDictionary<string, string> Answers,
    RetroStats Stats,
    bool StatsAreSnapshot,
    IReadOnlyList<RetroActionView> ToRevisit,
    IReadOnlyList<RetroActionView> Actions,
    IReadOnlyList<RetroView> Related,
    RetroView? Previous);

/// <summary>A retro the owner may want to do: its period has ended and none exists yet. Weekly retros are never suggested.</summary>
public sealed record RetroSuggestion(RetroType Type, DateOnly PeriodStart);

/// <summary>The guided questions for each retro type. Texts live in the resource file under Retro_Q_{key}.</summary>
public static class RetroQuestions
{
    public static IReadOnlyList<string> For(RetroType type) => type switch
    {
        RetroType.Weekly => ["went_well", "was_hard", "next_week"],
        RetroType.Monthly => ["highlights", "challenges", "learned", "change_next"],
        RetroType.Quarterly => ["achievements", "goals_review", "patterns", "next_quarter"],
        RetroType.Annual => ["year_story", "proud_of", "learned_year", "let_go", "next_year"],
        _ => [],
    };

    public static bool IsValid(RetroType type, string key) => For(type).Contains(key);
}
