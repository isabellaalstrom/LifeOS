using LifeOS.Domain.Common;
using LifeOS.Domain.Time;

namespace LifeOS.Domain.Retros;

public enum RetroType
{
    /// <summary>Optional. Never prompted for.</summary>
    Weekly,
    Monthly,
    Quarterly,
    Annual,
}

public enum RetroStatus
{
    Draft,
    Completed,
}

public enum RetroActionKind
{
    Action,
    Experiment,
}

public enum RetroActionStatus
{
    Open,
    InProgress,
    Done,
    Dropped,
}

public enum RetroOutcome
{
    Worked,
    Partly,
    DidNotWork,
    TooEarly,
}

/// <summary>
/// A reflection on one calendar period. One per type and period; it can be started at any time, saved as a draft,
/// resumed and completed. Everything except the title of the period is optional.
/// </summary>
public class Retrospective : OwnedEntity
{
    public RetroType Type { get; set; }

    /// <summary>First day of the period (Monday, first of month/quarter, 1 January).</summary>
    public DateOnly PeriodStart { get; set; }

    public RetroStatus Status { get; set; } = RetroStatus.Draft;

    public DateTimeOffset? CompletedUtc { get; set; }

    /// <summary>Free-text summary written by the owner.</summary>
    public string? Summary { get; set; }

    // Optional well-being reflections, 1-5. Only recorded if the owner chooses to.
    public int? Energy { get; set; }

    public int? Balance { get; set; }

    public int? Stress { get; set; }

    public int? Satisfaction { get; set; }

    /// <summary>Statistics as they were when the retro was completed, so the record does not drift as data changes.</summary>
    public string? StatsSnapshotJson { get; set; }
}

public class RetroResponse : OwnedEntity
{
    public Guid RetroId { get; set; }

    /// <summary>Key of a guided question (see RetroQuestions).</summary>
    public string QuestionKey { get; set; } = string.Empty;

    public string Answer { get; set; } = string.Empty;
}

/// <summary>An action or experiment decided in a retro. It stays open until the owner closes it, and is revisited in later retros.</summary>
public class RetroAction : OwnedEntity
{
    public Guid? RetroId { get; set; }

    public RetroActionKind Kind { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Details { get; set; }

    /// <summary>Who owns it. Free text, defaults to the owner themself.</summary>
    public string? OwnerName { get; set; }

    public DateOnly? StartDate { get; set; }

    public DateOnly? DueDate { get; set; }

    /// <summary>For experiments: what you expect to happen.</summary>
    public string? Hypothesis { get; set; }

    public RetroActionStatus Status { get; set; } = RetroActionStatus.Open;

    public DateTimeOffset? ClosedUtc { get; set; }

    /// <summary>Optional task created from this action (plain id, no constraint).</summary>
    public Guid? TaskItemId { get; set; }
}

/// <summary>What happened when an action was revisited. Appended, never edited.</summary>
public class RetroActionFollowUp : OwnedEntity
{
    public Guid ActionId { get; set; }

    /// <summary>The retro in which it was revisited, if any.</summary>
    public Guid? RetroId { get; set; }

    public DateTimeOffset RecordedUtc { get; set; }

    public RetroOutcome Outcome { get; set; }

    public string? Note { get; set; }
}

public static class RetroPeriods
{
    public static DateOnly Start(RetroType type, DateOnly date) => type switch
    {
        RetroType.Weekly => PeriodCalculator.WeekStart(date),
        RetroType.Monthly => PeriodCalculator.MonthStart(date),
        RetroType.Quarterly => PeriodCalculator.QuarterStart(date),
        RetroType.Annual => PeriodCalculator.YearStart(date),
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    public static DateOnly EndExclusive(RetroType type, DateOnly periodStart) => type switch
    {
        RetroType.Weekly => periodStart.AddDays(7),
        RetroType.Monthly => periodStart.AddMonths(1),
        RetroType.Quarterly => periodStart.AddMonths(3),
        RetroType.Annual => periodStart.AddYears(1),
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    public static DateOnly Previous(RetroType type, DateOnly periodStart) => type switch
    {
        RetroType.Weekly => periodStart.AddDays(-7),
        RetroType.Monthly => periodStart.AddMonths(-1),
        RetroType.Quarterly => periodStart.AddMonths(-3),
        RetroType.Annual => periodStart.AddYears(-1),
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    /// <summary>The retro type that summarizes the smaller periods inside this one (annual to quarterly, quarterly to monthly, monthly to weekly).</summary>
    public static RetroType? SmallerType(RetroType type) => type switch
    {
        RetroType.Annual => RetroType.Quarterly,
        RetroType.Quarterly => RetroType.Monthly,
        RetroType.Monthly => RetroType.Weekly,
        _ => null,
    };
}
