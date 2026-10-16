using LifeOS.Domain.Common;
using LifeOS.Domain.LifeAreas;
using LifeOS.Domain.Projects;

namespace LifeOS.Domain.Tasks;

/// <summary>Where an unplanned task lives. Planning into a sprint is separate (SprintTask).</summary>
public enum TaskTriage
{
    Inbox,
    Backlog,
}

public enum TaskItemStatus
{
    ToDo,
    InProgress,
    Done,
    Cancelled,
}

public enum TaskPriority
{
    None,
    Low,
    Medium,
    High,
}

public class TaskItem : OwnedEntity
{
    public string Title { get; set; } = string.Empty;

    public string? Notes { get; set; }

    public TaskTriage Triage { get; set; } = TaskTriage.Inbox;

    public TaskItemStatus Status { get; set; } = TaskItemStatus.ToDo;

    public TaskPriority Priority { get; set; } = TaskPriority.None;

    public int? EstimateMinutes { get; set; }

    public DateOnly? DueDate { get; set; }

    /// <summary>Optional calendar slot; never required.</summary>
    public DateOnly? ScheduledDate { get; set; }

    public TimeOnly? ScheduledTime { get; set; }

    /// <summary>The day this task was picked as daily focus (user-local date).</summary>
    public DateOnly? FocusDate { get; set; }

    public Guid? LifeAreaId { get; set; }

    public LifeArea? LifeArea { get; set; }

    public Guid? ProjectId { get; set; }

    public Project? Project { get; set; }

    /// <summary>Set for subtasks. Only one level of nesting is allowed.</summary>
    public Guid? ParentTaskId { get; set; }

    public TaskItem? Parent { get; set; }

    public ICollection<TaskItem> Subtasks { get; set; } = new List<TaskItem>();

    public DateTimeOffset? CompletedUtc { get; set; }

    public bool IsOpen => Status is TaskItemStatus.ToDo or TaskItemStatus.InProgress;
}
