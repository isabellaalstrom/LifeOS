using LifeOS.Domain.Common;
using LifeOS.Domain.LifeAreas;

namespace LifeOS.Domain.Goals;

/// <summary>Planning horizon. Ordered long to short: a parent goal always has a smaller value than its children.</summary>
public enum GoalHorizon
{
    Year,
    Quarter,
    Month,
}

public enum GoalStatus
{
    Active,
    Achieved,
    Paused,
    Dropped,
}

public enum GoalProgressMode
{
    /// <summary>Start, current and target values with a unit; works for increasing and decreasing targets.</summary>
    Numeric,

    /// <summary>Progress is finished milestones out of all milestones.</summary>
    Milestones,

    /// <summary>Progress is finished tasks out of linked tasks (directly or through a linked project).</summary>
    LinkedTasks,

    /// <summary>The owner records a percentage by hand.</summary>
    Manual,
}

public enum GoalProgressSource
{
    Created,
    Manual,
    TaskChanged,
    Milestone,
    Habit,
    TargetChanged,
}

public class Goal : OwnedEntity
{
    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public GoalHorizon Horizon { get; set; }

    /// <summary>First day of the calendar period (1 Jan, first day of the quarter, first day of the month).</summary>
    public DateOnly PeriodStart { get; set; }

    public Guid? LifeAreaId { get; set; }

    public LifeArea? LifeArea { get; set; }

    public Guid? ParentGoalId { get; set; }

    public Goal? Parent { get; set; }

    public ICollection<Goal> Children { get; set; } = new List<Goal>();

    public GoalStatus Status { get; set; } = GoalStatus.Active;

    public GoalProgressMode Mode { get; set; } = GoalProgressMode.Manual;

    public decimal? StartValue { get; set; }

    public decimal? TargetValue { get; set; }

    public decimal? CurrentValue { get; set; }

    public string? Unit { get; set; }

    /// <summary>0-100, used by the manual mode.</summary>
    public decimal ManualPercent { get; set; }

    public DateTimeOffset? AchievedUtc { get; set; }

    public ICollection<GoalMilestone> Milestones { get; set; } = new List<GoalMilestone>();
}

public class GoalMilestone : OwnedEntity
{
    public Guid GoalId { get; set; }

    public Goal Goal { get; set; } = null!;

    public string Title { get; set; } = string.Empty;

    public DateOnly? DueDate { get; set; }

    public DateTimeOffset? CompletedUtc { get; set; }

    public int SortOrder { get; set; }

    public bool IsDone => CompletedUtc is not null;
}

/// <summary>Timestamped progress snapshot for graphs and retrospective comparisons. Append-only.</summary>
public class GoalProgressEntry : OwnedEntity
{
    public Guid GoalId { get; set; }

    public DateTimeOffset RecordedUtc { get; set; }

    /// <summary>0-100 at the time.</summary>
    public decimal Percent { get; set; }

    /// <summary>Numeric value at the time (numeric goals only).</summary>
    public decimal? Value { get; set; }

    /// <summary>Target at the time, so history stays readable if the target is changed later.</summary>
    public decimal? Target { get; set; }

    public GoalProgressSource Source { get; set; }

    public string? Note { get; set; }
}
