namespace LifeOS.Domain.Goals;

/// <summary>Calendar periods for goals: a year, a quarter or a month, identified by their first day.</summary>
public static class GoalPeriods
{
    public static DateOnly Start(GoalHorizon horizon, DateOnly date) => horizon switch
    {
        GoalHorizon.Year => new DateOnly(date.Year, 1, 1),
        GoalHorizon.Quarter => new DateOnly(date.Year, ((date.Month - 1) / 3) * 3 + 1, 1),
        GoalHorizon.Month => new DateOnly(date.Year, date.Month, 1),
        _ => throw new ArgumentOutOfRangeException(nameof(horizon)),
    };

    public static DateOnly EndExclusive(GoalHorizon horizon, DateOnly periodStart) => horizon switch
    {
        GoalHorizon.Year => periodStart.AddYears(1),
        GoalHorizon.Quarter => periodStart.AddMonths(3),
        GoalHorizon.Month => periodStart.AddMonths(1),
        _ => throw new ArgumentOutOfRangeException(nameof(horizon)),
    };

    public static DateOnly Shift(GoalHorizon horizon, DateOnly periodStart, int periods) => horizon switch
    {
        GoalHorizon.Year => periodStart.AddYears(periods),
        GoalHorizon.Quarter => periodStart.AddMonths(3 * periods),
        GoalHorizon.Month => periodStart.AddMonths(periods),
        _ => throw new ArgumentOutOfRangeException(nameof(horizon)),
    };

    public static bool ContainsDate(GoalHorizon horizon, DateOnly periodStart, DateOnly date) =>
        date >= periodStart && date < EndExclusive(horizon, periodStart);

    /// <summary>A parent must have a longer horizon than its child.</summary>
    public static bool IsValidParentHorizon(GoalHorizon parent, GoalHorizon child) => parent < child;

    /// <summary>True when the child's whole period lies inside the parent's period.</summary>
    public static bool IsInside(GoalHorizon parentHorizon, DateOnly parentStart, GoalHorizon childHorizon, DateOnly childStart) =>
        childStart >= parentStart && EndExclusive(childHorizon, childStart) <= EndExclusive(parentHorizon, parentStart);
}

/// <summary>Progress arithmetic for goals. All results are percentages in the range 0-100.</summary>
public static class GoalProgressCalculator
{
    public static decimal Clamp(decimal percent) => Math.Min(100m, Math.Max(0m, percent));

    /// <summary>
    /// Numeric progress from start towards target. Works for decreasing targets (target below start).
    /// When start equals target there is no distance to cover: the goal is complete exactly when the value equals the target.
    /// Values beyond the target are capped at 100, movement away from the target is floored at 0.
    /// </summary>
    public static decimal Numeric(decimal start, decimal target, decimal current)
    {
        var distance = target - start;
        if (distance == 0m)
        {
            return current == target ? 100m : 0m;
        }

        return Clamp((current - start) / distance * 100m);
    }

    /// <summary>True when the value has gone past the target (in the direction of the goal).</summary>
    public static bool IsBeyondTarget(decimal start, decimal target, decimal current) =>
        target >= start ? current > target : current < target;

    public static decimal Ratio(int done, int total) => total <= 0 ? 0m : Clamp(done * 100m / total);

    /// <summary>Progress for a goal in whatever mode it uses. Empty milestone/task sets give 0 rather than an error.</summary>
    public static decimal ForGoal(Goal goal, int milestonesDone, int milestonesTotal, int tasksDone, int tasksTotal) => goal.Mode switch
    {
        GoalProgressMode.Numeric => goal is { StartValue: { } start, TargetValue: { } target }
            ? Numeric(start, target, goal.CurrentValue ?? start)
            : 0m,
        GoalProgressMode.Milestones => Ratio(milestonesDone, milestonesTotal),
        GoalProgressMode.LinkedTasks => Ratio(tasksDone, tasksTotal),
        GoalProgressMode.Manual => Clamp(goal.ManualPercent),
        _ => 0m,
    };
}
