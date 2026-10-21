using LifeOS.Application.Abstractions;
using LifeOS.Application.Organize;
using LifeOS.Domain.Reminders;
using LifeOS.Domain.Tasks;
using Microsoft.EntityFrameworkCore;

namespace LifeOS.Application.Reminders;

public enum ReminderSyncStatus
{
    /// <summary>Synced (possibly with nothing to do).</summary>
    Ok,

    /// <summary>No Home Assistant list is configured on this server.</summary>
    NotConfigured,

    /// <summary>The owner has not switched import or sending on.</summary>
    Disabled,

    /// <summary>The list could not be read or written; nothing was changed in LifeOS this time.</summary>
    Failed,
}

public sealed record ReminderSyncResult(ReminderSyncStatus Status, int Imported = 0, int Pushed = 0, int Updated = 0);

public sealed record ReminderStatusView(
    bool Configured,
    string? ListName,
    bool ImportEnabled,
    bool PushEnabled,
    DateTimeOffset? LastSyncUtc,
    string? LastError);

/// <summary>
/// Keeps tasks and the owner's reminders list in step (through Home Assistant). Each side is compared with a snapshot taken
/// after the previous sync, so the side that changed wins and an old value never overwrites a newer one:
/// done/not done and the title follow whichever side changed; a due date follows the reminder, and LifeOS-side date
/// changes are sent when the reminder is unchanged. A reminder deleted on the phone leaves the task as it is and is not sent again.
/// </summary>
public sealed class ReminderSyncService(
    IAppDbContextFactory dbFactory,
    IUserClock clock,
    IReminderClient client,
    TaskService tasks)
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    /// <summary>At most this many tasks are sent in one pass, so a large backlog never floods the phone.</summary>
    public const int MaxPushPerPass = 20;

    /// <summary>A reminder LifeOS added that never appeared in the list within this time is forgotten.</summary>
    private static readonly TimeSpan PendingTimeout = TimeSpan.FromHours(24);

    public async Task<ReminderStatusView> GetStatusAsync(CancellationToken ct = default)
    {
        await using var db = dbFactory.Create();
        var s = await db.ReminderSettings.FirstOrDefaultAsync(ct);
        return new ReminderStatusView(client.IsConfigured, client.ListName, s?.ImportEnabled ?? false, s?.PushEnabled ?? false, s?.LastSyncUtc, s?.LastError);
    }

    public async Task SetEnabledAsync(bool import, bool push, CancellationToken ct = default)
    {
        await using var db = dbFactory.Create();
        var s = await db.ReminderSettings.FirstOrDefaultAsync(ct);
        if (s is null)
        {
            s = new ReminderSettings();
            db.ReminderSettings.Add(s);
        }

        s.ImportEnabled = import;
        s.PushEnabled = push;
        await db.SaveChangesAsync(ct);
    }

    public async Task<ReminderSyncResult> SyncAsync(CancellationToken ct = default)
    {
        if (!client.IsConfigured)
        {
            return new ReminderSyncResult(ReminderSyncStatus.NotConfigured);
        }

        await using (var peek = dbFactory.Create())
        {
            var s = await peek.ReminderSettings.FirstOrDefaultAsync(ct);
            if (s is null || !(s.ImportEnabled || s.PushEnabled))
            {
                return new ReminderSyncResult(ReminderSyncStatus.Disabled);
            }
        }

        await Gate.WaitAsync(ct);
        try
        {
            return await SyncCoreAsync(ct);
        }
        finally
        {
            Gate.Release();
        }
    }

    private async Task<ReminderSyncResult> SyncCoreAsync(CancellationToken ct)
    {
        int imported = 0, pushed = 0, updated = 0;
        try
        {
            var items = await client.GetItemsAsync(ct);
            var byUid = items.ToDictionary(i => i.Uid);
            await using var db = dbFactory.Create();
            var settings = await db.ReminderSettings.FirstAsync(ct);
            var now = clock.UtcNow;

            var links = await db.ReminderLinks.ToListAsync(ct);
            var taskIds = links.Select(l => l.TaskItemId).ToList();
            var linkedTasks = await db.Tasks.Where(t => taskIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, ct);

            // 1. Pending links: the reminder LifeOS added has now appeared (matched by title among items nobody owns yet).
            var claimed = links.Where(l => l.ReminderUid is not null).Select(l => l.ReminderUid!).ToHashSet();
            foreach (var pending in links.Where(l => l.ReminderUid is null).OrderBy(l => l.PendingSinceUtc).ToList())
            {
                var match = items.FirstOrDefault(i => !claimed.Contains(i.Uid) && i.Summary == pending.LastTitle);
                if (match is not null)
                {
                    pending.ReminderUid = match.Uid;
                    pending.PendingSinceUtc = null;
                    claimed.Add(match.Uid);
                }
                else if (now - (pending.PendingSinceUtc ?? now) > PendingTimeout)
                {
                    db.ReminderLinks.Remove(pending);
                    links.Remove(pending);
                }
            }

            // 2. Linked pairs: whichever side changed since the last sync wins.
            foreach (var link in links.Where(l => l.ReminderUid is not null).ToList())
            {
                if (!linkedTasks.TryGetValue(link.TaskItemId, out var task))
                {
                    db.ReminderLinks.Remove(link);
                    continue;
                }

                if (!byUid.TryGetValue(link.ReminderUid!, out var item))
                {
                    // Gone from the phone (deleted or cleaned up): leave the task alone, and keep the link so it is not sent again.
                    continue;
                }

                var remDue = DueOf(item);
                var lifeDone = task.Status == TaskItemStatus.Done;

                // done / not done
                var remChanged = item.Done != link.LastDone;
                var lifeChanged = lifeDone != link.LastDone;
                var newDone = remChanged ? item.Done : lifeChanged ? lifeDone : link.LastDone;
                if (newDone != lifeDone)
                {
                    await tasks.SetStatusAsync(task.Id, newDone ? TaskItemStatus.Done : TaskItemStatus.ToDo, ct);
                    updated++;
                }

                // title
                string? pushTitle = null;
                var newTitle = link.LastTitle;
                if (item.Summary != link.LastTitle)
                {
                    task.Title = Truncate(item.Summary);
                    newTitle = item.Summary;
                    updated++;
                }
                else if (task.Title != link.LastTitle)
                {
                    pushTitle = task.Title;
                    newTitle = task.Title;
                }

                // due date (a date can be set from LifeOS; clearing it is only possible on the phone)
                bool pushDue = false;
                var newDue = link.LastDue;
                if (remDue != link.LastDue)
                {
                    task.DueDate = remDue;
                    newDue = remDue;
                    updated++;
                }
                else if (task.DueDate != link.LastDue && task.DueDate is not null)
                {
                    pushDue = true;
                    newDue = task.DueDate;
                }

                if (pushTitle is not null || pushDue || newDone != item.Done)
                {
                    await client.UpdateAsync(link.ReminderUid!, pushTitle, pushDue ? newDue : null, newDone != item.Done ? newDone : null, ct);
                }

                link.LastTitle = newTitle;
                link.LastDue = newDue;
                link.LastDone = newDone;
            }

            await db.SaveChangesAsync(ct);

            // 3. Import: open reminders nobody owns yet become tasks in the backlog.
            if (settings.ImportEnabled)
            {
                claimed = (await db.ReminderLinks.Where(l => l.ReminderUid != null).Select(l => l.ReminderUid!).ToListAsync(ct)).ToHashSet();
                foreach (var item in items.Where(i => !i.Done && !claimed.Contains(i.Uid)))
                {
                    var due = DueOf(item);
                    var taskId = await tasks.CreateAsync(new TaskInput(Truncate(item.Summary), DueDate: due), ct);
                    await using var linkDb = dbFactory.Create();
                    linkDb.ReminderLinks.Add(new ReminderLink
                    {
                        TaskItemId = taskId,
                        ReminderUid = item.Uid,
                        LastTitle = item.Summary,
                        LastDue = due,
                        LastDone = false,
                    });
                    await linkDb.SaveChangesAsync(ct);
                    imported++;
                }
            }

            // 4. Push: tasks that are due or in today's focus go to the list, once.
            if (settings.PushEnabled)
            {
                var today = clock.Today;
                var linkedIds = (await db.ReminderLinks.Select(l => l.TaskItemId).ToListAsync(ct)).ToHashSet();
                var candidates = (await db.Tasks
                        .Where(t => t.ParentTaskId == null && (t.Status == TaskItemStatus.ToDo || t.Status == TaskItemStatus.InProgress)
                                    && (t.FocusDate == today || (t.DueDate != null && t.DueDate <= today)))
                        .OrderBy(t => t.CreatedUtc)
                        .ToListAsync(ct))
                    .Where(t => !linkedIds.Contains(t.Id))
                    .Take(MaxPushPerPass)
                    .ToList();

                foreach (var task in candidates)
                {
                    var due = task.DueDate ?? today;
                    await client.AddAsync(task.Title, due, ct);
                    db.ReminderLinks.Add(new ReminderLink
                    {
                        TaskItemId = task.Id,
                        ReminderUid = null,
                        LastTitle = task.Title,
                        LastDue = due,
                        LastDone = false,
                        PendingSinceUtc = now,
                    });
                    // Saved one by one: if a later call fails, the ones already sent stay tracked and are not sent again.
                    await db.SaveChangesAsync(ct);
                    pushed++;
                }
            }

            settings.LastSyncUtc = now;
            settings.LastError = null;
            await db.SaveChangesAsync(ct);
            return new ReminderSyncResult(ReminderSyncStatus.Ok, imported, pushed, updated);
        }
        catch (ReminderException ex)
        {
            await RecordFailureAsync(ex.Message, ct);
            return new ReminderSyncResult(ReminderSyncStatus.Failed, imported, pushed, updated);
        }
    }

    private async Task RecordFailureAsync(string message, CancellationToken ct)
    {
        await using var db = dbFactory.Create();
        var s = await db.ReminderSettings.FirstOrDefaultAsync(ct);
        if (s is not null)
        {
            s.LastError = message.Length > 200 ? message[..200] : message;
            await db.SaveChangesAsync(ct);
        }
    }

    private DateOnly? DueOf(ReminderItem item) => item.Due
        ?? (item.DueAt is { } at ? DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, clock.Zone).DateTime) : null);

    private static string Truncate(string title) =>
        title.Length > TaskService.MaxTitleLength ? title[..TaskService.MaxTitleLength] : title;
}
