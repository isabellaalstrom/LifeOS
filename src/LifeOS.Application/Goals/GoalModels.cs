using LifeOS.Application.Habits;
using LifeOS.Application.Organize;
using LifeOS.Domain.Goals;

namespace LifeOS.Application.Goals;

public sealed record GoalInput(
    string Title,
    GoalHorizon Horizon,
    DateOnly PeriodStart,
    GoalProgressMode Mode,
    string? Description = null,
    Guid? LifeAreaId = null,
    Guid? ParentGoalId = null,
    decimal? StartValue = null,
    decimal? TargetValue = null,
    decimal? CurrentValue = null,
    string? Unit = null);

public sealed record GoalView(
    Guid Id,
    string Title,
    string? Description,
    GoalHorizon Horizon,
    DateOnly PeriodStart,
    Guid? LifeAreaId,
    string? LifeAreaName,
    Guid? ParentGoalId,
    GoalStatus Status,
    GoalProgressMode Mode,
    decimal Percent,
    decimal? StartValue,
    decimal? TargetValue,
    decimal? CurrentValue,
    string? Unit,
    decimal ManualPercent,
    int MilestonesDone,
    int MilestonesTotal,
    int TasksDone,
    int TasksTotal,
    bool IsBeyondTarget);

public sealed record MilestoneView(Guid Id, string Title, DateOnly? DueDate, bool IsDone, int SortOrder);

public sealed record GoalHistoryPoint(
    DateTimeOffset RecordedUtc,
    decimal Percent,
    decimal? Value,
    decimal? Target,
    GoalProgressSource Source,
    string? Note);

public sealed record GoalDetail(
    GoalView Goal,
    IReadOnlyList<MilestoneView> Milestones,
    IReadOnlyList<GoalHistoryPoint> History,
    IReadOnlyList<GoalView> Children,
    IReadOnlyList<TaskView> LinkedTasks,
    IReadOnlyList<ProjectView> Projects,
    IReadOnlyList<HabitView> Habits);

/// <summary>Counts that, with the goal itself, determine its progress.</summary>
public sealed record GoalCounts(int MilestonesDone, int MilestonesTotal, int TasksDone, int TasksTotal)
{
    public static readonly GoalCounts Empty = new(0, 0, 0, 0);
}
