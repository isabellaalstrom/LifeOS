using LifeOS.Domain.Common;
using LifeOS.Domain.Goals;
using LifeOS.Domain.LifeAreas;

namespace LifeOS.Domain.Habits;

public enum HabitPeriod
{
    Day,
    Week,
    Month,
}

public class Habit : OwnedEntity
{
    public string Name { get; set; } = string.Empty;

    public string? Notes { get; set; }

    /// <summary>The recurring target is <see cref="TargetCount"/> completions per period (e.g. 3 per week).</summary>
    public HabitPeriod Period { get; set; } = HabitPeriod.Week;

    public int TargetCount { get; set; } = 1;

    public Guid? LifeAreaId { get; set; }

    public LifeArea? LifeArea { get; set; }

    public Guid? GoalId { get; set; }

    public Goal? Goal { get; set; }

    /// <summary>Added to a linked numeric goal for each completion.</summary>
    public decimal GoalIncrement { get; set; } = 1m;

    /// <summary>Estimate given to generated sprint tasks.</summary>
    public int? EstimateMinutes { get; set; }

    /// <summary>Weekly habits only: generate one sprint task per planned occurrence when asked.</summary>
    public bool GenerateSprintTasks { get; set; }

    public bool IsArchived { get; set; }
}

public class HabitCompletion : OwnedEntity
{
    public Guid HabitId { get; set; }

    public Habit Habit { get; set; } = null!;

    /// <summary>The user-local day the habit was done.</summary>
    public DateOnly Date { get; set; }

    public DateTimeOffset CompletedUtc { get; set; }

    public string? Note { get; set; }

    /// <summary>Set when this completion came from finishing a generated sprint task. Unique, so it can never be counted twice.</summary>
    public Guid? TaskItemId { get; set; }
}
