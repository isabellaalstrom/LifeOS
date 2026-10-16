using LifeOS.Domain.Common;
using LifeOS.Domain.Tasks;

namespace LifeOS.Domain.Planning;

/// <summary>A weekly sprint, identified by the Monday (user-local) the week starts on.</summary>
public class Sprint : OwnedEntity
{
    public DateOnly WeekStart { get; set; }

    public string? Goal { get; set; }

    public int? CapacityMinutes { get; set; }

    /// <summary>
    /// Optional explicit "start sprint". The scope boundary is this instant if set, otherwise the
    /// start of the week in the user's time zone.
    /// </summary>
    public DateTimeOffset? CommittedUtc { get; set; }

    public DateTimeOffset? ClosedUtc { get; set; }

    public bool IsClosed => ClosedUtc is not null;

    public ICollection<SprintTask> Tasks { get; set; } = new List<SprintTask>();
}

/// <summary>
/// A task's membership in a sprint. Rows are never deleted so a closed sprint keeps its history;
/// a task that left has LeftUtc set.
/// </summary>
public class SprintTask : OwnedEntity
{
    public Guid SprintId { get; set; }

    public Sprint Sprint { get; set; } = null!;

    public Guid TaskItemId { get; set; }

    public TaskItem Task { get; set; } = null!;

    public int Position { get; set; }

    public DateTimeOffset AddedUtc { get; set; }

    public DateTimeOffset? LeftUtc { get; set; }

    public ScopeChangeReason? LeaveReason { get; set; }

    public bool IsActive => LeftUtc is null;
}

public enum ScopeChangeKind
{
    Added,
    Removed,
}

public enum ScopeChangeReason
{
    /// <summary>Added by the user (planning).</summary>
    Planned,

    /// <summary>Moved to another sprint by the user.</summary>
    Rescheduled,

    /// <summary>Moved on when the sprint was closed.</summary>
    CarryOver,

    /// <summary>Taken out of the sprint by the user (stays in the backlog).</summary>
    Dropped,

    /// <summary>Unfinished work sent back to the backlog when the sprint was closed.</summary>
    ReturnedToBacklog,
}

/// <summary>Append-only audit of sprint scope. Never updated or deleted.</summary>
public class SprintScopeChange : OwnedEntity
{
    public Guid SprintId { get; set; }

    public Guid TaskItemId { get; set; }

    /// <summary>Snapshot so history stays readable if the task is later renamed.</summary>
    public string TaskTitle { get; set; } = string.Empty;

    public int? EstimateMinutes { get; set; }

    public ScopeChangeKind Kind { get; set; }

    public ScopeChangeReason Reason { get; set; }

    public DateTimeOffset OccurredUtc { get; set; }

    /// <summary>The other sprint involved in a move or carry-over.</summary>
    public Guid? RelatedSprintId { get; set; }
}
