using LifeOS.Domain.Habits;

namespace LifeOS.Application.Habits;

public sealed record HabitInput(
    string Name,
    HabitPeriod Period,
    int TargetCount,
    string? Notes = null,
    Guid? LifeAreaId = null,
    Guid? GoalId = null,
    decimal GoalIncrement = 1m,
    int? EstimateMinutes = null,
    bool GenerateSprintTasks = false);

public sealed record HabitView(
    Guid Id,
    string Name,
    string? Notes,
    HabitPeriod Period,
    int TargetCount,
    Guid? LifeAreaId,
    string? LifeAreaName,
    Guid? GoalId,
    string? GoalTitle,
    decimal GoalIncrement,
    int? EstimateMinutes,
    bool GenerateSprintTasks,
    bool IsArchived,
    DateOnly PeriodStart,
    int CountInPeriod,
    bool TargetMet,
    int Streak,
    Guid? OpenGeneratedTaskId,
    int LoggedToday = 0);

public sealed record HabitPeriodStat(DateOnly PeriodStart, int Count, int Target)
{
    public bool Met => Count >= Target;
}

public sealed record HabitCompletionView(Guid Id, DateOnly Date, DateTimeOffset CompletedUtc, string? Note, bool FromTask);

public sealed record HabitDetail(
    HabitView Habit,
    IReadOnlyList<HabitPeriodStat> RecentPeriods,
    IReadOnlyList<HabitCompletionView> Completions);

public sealed record GenerateHabitTasksResult(int Created, int AddedToSprint);
