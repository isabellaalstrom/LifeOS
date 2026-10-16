using LifeOS.Domain.Planning;
using LifeOS.Domain.Projects;
using LifeOS.Domain.Tasks;

namespace LifeOS.Application.Organize;

public sealed record TaskInput(
    string Title,
    string? Notes = null,
    TaskPriority Priority = TaskPriority.None,
    int? EstimateMinutes = null,
    DateOnly? DueDate = null,
    DateOnly? ScheduledDate = null,
    TimeOnly? ScheduledTime = null,
    Guid? LifeAreaId = null,
    Guid? ProjectId = null);

public sealed record TaskView(
    Guid Id,
    string Title,
    string? Notes,
    TaskTriage Triage,
    TaskItemStatus Status,
    TaskPriority Priority,
    int? EstimateMinutes,
    DateOnly? DueDate,
    DateOnly? ScheduledDate,
    TimeOnly? ScheduledTime,
    DateOnly? FocusDate,
    Guid? LifeAreaId,
    string? LifeAreaName,
    string? LifeAreaColor,
    Guid? ProjectId,
    string? ProjectName,
    Guid? ParentTaskId,
    int SubtaskTotal,
    int SubtaskDone,
    DateOnly? PlannedWeekStart,
    DateTimeOffset? CompletedUtc)
{
    public bool IsOpen => Status is TaskItemStatus.ToDo or TaskItemStatus.InProgress;
}

public sealed record TaskDetail(TaskView Task, IReadOnlyList<TaskView> Subtasks);

public sealed record TaskFilter(Guid? LifeAreaId = null, Guid? ProjectId = null, string? Text = null);

public enum DeleteOutcome
{
    Deleted,

    /// <summary>The task has sprint history, so it was cancelled instead of removed.</summary>
    Cancelled,
}

public sealed record ProjectInput(string Name, string? Description = null, Guid? LifeAreaId = null);

public sealed record ProjectView(
    Guid Id,
    string Name,
    string? Description,
    Guid? LifeAreaId,
    string? LifeAreaName,
    ProjectStatus Status,
    int OpenTasks,
    int DoneTasks);

public enum CarryDecision
{
    NextSprint,
    Backlog,
}

public sealed record SprintBoardTask(
    TaskView Task,
    int Position,
    bool IsActive,
    ScopeChangeReason? LeaveReason,
    DateTimeOffset AddedUtc);

public sealed record SprintBoard(
    DateOnly WeekStart,
    int IsoWeek,
    Guid? SprintId,
    string? Goal,
    int? CapacityMinutes,
    int PlannedMinutes,
    CapacityStatus Capacity,
    bool IsCurrent,
    bool IsCommitted,
    DateTimeOffset? CommittedUtc,
    DateTimeOffset BoundaryUtc,
    bool IsClosed,
    IReadOnlyList<SprintBoardTask> Tasks,
    SprintScopeStats Stats)
{
    public IEnumerable<SprintBoardTask> ActiveTasks => Tasks.Where(t => t.IsActive);
}

public sealed record ScopeChangeView(
    Guid TaskItemId,
    string TaskTitle,
    ScopeChangeKind Kind,
    ScopeChangeReason Reason,
    DateTimeOffset OccurredUtc,
    bool AfterBoundary);

public sealed record PastSprintToClose(DateOnly WeekStart, int IsoWeek, int OpenTasks);

public sealed record TodayView(
    DateOnly Date,
    IReadOnlyList<TaskView> Focus,
    IReadOnlyList<TaskView> DueOrOverdue,
    IReadOnlyList<TaskView> SprintSuggestions);

public sealed record SprintSummary(
    DateOnly WeekStart,
    int IsoWeek,
    bool Exists,
    string? Goal,
    int ToDo,
    int InProgress,
    int Done,
    int PlannedMinutes,
    int? CapacityMinutes,
    CapacityStatus Capacity,
    SprintScopeStats Stats);

public sealed record OverviewView(
    DateOnly Date,
    IReadOnlyList<TaskView> Focus,
    SprintSummary Sprint,
    IReadOnlyList<TaskView> Deadlines,
    int InboxCount,
    IReadOnlyList<PastSprintToClose> PastSprintsToClose);
