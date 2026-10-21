using LifeOS.Domain.Common;
using LifeOS.Domain.Tasks;

namespace LifeOS.Domain.Reminders;

/// <summary>Per-owner switches for the Reminders connection (through Home Assistant). Both are off until the owner turns them on.</summary>
public class ReminderSettings : OwnedEntity
{
    /// <summary>New open reminders in the connected list become tasks in the backlog.</summary>
    public bool ImportEnabled { get; set; }

    /// <summary>Tasks that are due or in today's focus are added to the connected list.</summary>
    public bool PushEnabled { get; set; }

    public DateTimeOffset? LastSyncUtc { get; set; }

    /// <summary>A short, non-sensitive reason for the last failure, or null.</summary>
    public string? LastError { get; set; }
}

/// <summary>
/// Ties a task to a reminder. The "last" fields are what both sides looked like after the previous sync; comparing each
/// side to them tells which side changed, so a change is never overwritten by the other side's older value.
/// A link with no <see cref="ReminderUid"/> is pending: LifeOS added the reminder but it has not shown up in the list yet
/// (iCloud can lag), and it is matched by title once it does so it is never imported as a second task.
/// </summary>
public class ReminderLink : OwnedEntity
{
    public Guid TaskItemId { get; set; }

    public TaskItem? Task { get; set; }

    public string? ReminderUid { get; set; }

    public string LastTitle { get; set; } = string.Empty;

    public DateOnly? LastDue { get; set; }

    public bool LastDone { get; set; }

    /// <summary>When a pending link was created; old pending links are dropped.</summary>
    public DateTimeOffset? PendingSinceUtc { get; set; }
}
