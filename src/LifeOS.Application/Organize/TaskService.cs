using LifeOS.Application.Abstractions;
using LifeOS.Application.Planning;
using LifeOS.Domain.Tasks;
using Microsoft.EntityFrameworkCore;

namespace LifeOS.Application.Organize;

public sealed class TaskService(IAppDbContextFactory dbFactory, IUserClock clock)
{
    public const int MaxTitleLength = 300;
    public const int MaxEstimateMinutes = 10_000;

    /// <summary>Quick capture: no categorization required, lands in the Inbox.</summary>
    public async Task<Guid> CaptureAsync(string title, CancellationToken ct = default)
    {
        title = RequireTitle(title);
        await using var db = dbFactory.Create();
        var task = new TaskItem { Title = title, Triage = TaskTriage.Inbox };
        db.Tasks.Add(task);
        await db.SaveChangesAsync(ct);
        return task.Id;
    }

    public async Task<Guid> CreateAsync(TaskInput input, TaskTriage triage = TaskTriage.Backlog, CancellationToken ct = default)
    {
        await using var db = dbFactory.Create();
        var task = new TaskItem { Triage = triage };
        Apply(task, input);
        db.Tasks.Add(task);
        await db.SaveChangesAsync(ct);
        return task.Id;
    }

    public async Task UpdateAsync(Guid id, TaskInput input, CancellationToken ct = default)
    {
        await using var db = dbFactory.Create();
        var task = await Load(db, id, ct);
        Apply(task, input);
        await db.SaveChangesAsync(ct);
    }

    public async Task<Guid> AddSubtaskAsync(Guid parentId, string title, CancellationToken ct = default)
    {
        title = RequireTitle(title);
        await using var db = dbFactory.Create();
        var parent = await Load(db, parentId, ct);
        if (parent.ParentTaskId is not null)
        {
            throw new PlanningException(PlanningException.Codes.SubtaskTooDeep);
        }

        var sub = new TaskItem
        {
            Title = title,
            ParentTaskId = parent.Id,
            Triage = TaskTriage.Backlog,
            LifeAreaId = parent.LifeAreaId,
            ProjectId = parent.ProjectId,
        };
        db.Tasks.Add(sub);
        await db.SaveChangesAsync(ct);
        return sub.Id;
    }

    public async Task SetStatusAsync(Guid id, TaskItemStatus status, CancellationToken ct = default)
    {
        await using var db = dbFactory.Create();
        var task = await Load(db, id, ct);
        if (task.Status == status)
        {
            return;
        }

        task.Status = status;
        task.CompletedUtc = status == TaskItemStatus.Done ? clock.UtcNow : null;
        await db.SaveChangesAsync(ct);
    }

    public async Task SetTriageAsync(Guid id, TaskTriage triage, CancellationToken ct = default)
    {
        await using var db = dbFactory.Create();
        var task = await Load(db, id, ct);
        task.Triage = triage;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Marks (or clears, with null) the task as focus for a user-local day.</summary>
    public async Task SetFocusAsync(Guid id, DateOnly? day, CancellationToken ct = default)
    {
        await using var db = dbFactory.Create();
        var task = await Load(db, id, ct);
        task.FocusDate = day;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Removes a task. Tasks with sprint history are cancelled instead, so retros stay accurate.</summary>
    public async Task<DeleteOutcome> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = dbFactory.Create();
        var task = await Load(db, id, ct);
        var ids = await db.Tasks.Where(t => t.Id == id || t.ParentTaskId == id).Select(t => t.Id).ToListAsync(ct);

        var hasHistory = await db.SprintTasks.AnyAsync(st => ids.Contains(st.TaskItemId), ct)
                         || await db.ScopeChanges.AnyAsync(c => ids.Contains(c.TaskItemId), ct);

        var family = await db.Tasks.Where(t => ids.Contains(t.Id)).ToListAsync(ct);
        if (hasHistory)
        {
            foreach (var t in family)
            {
                t.Status = TaskItemStatus.Cancelled;
                t.CompletedUtc = null;
                t.FocusDate = null;
            }

            await db.SaveChangesAsync(ct);
            return DeleteOutcome.Cancelled;
        }

        db.Tasks.RemoveRange(family.Where(t => t.Id != task.Id));
        await db.SaveChangesAsync(ct);
        db.Tasks.Remove(task);
        await db.SaveChangesAsync(ct);
        return DeleteOutcome.Deleted;
    }

    public async Task<TaskDetail?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = dbFactory.Create();
        var task = await db.Tasks.Where(t => t.Id == id).ToView(db.SprintTasks).FirstOrDefaultAsync(ct);
        if (task is null)
        {
            return null;
        }

        var subtasks = await db.Tasks.Where(t => t.ParentTaskId == id)
            .OrderBy(t => t.CreatedUtc)
            .ToView(db.SprintTasks)
            .ToListAsync(ct);
        return new TaskDetail(task, subtasks);
    }

    public async Task<IReadOnlyList<TaskView>> ListInboxAsync(CancellationToken ct = default)
    {
        await using var db = dbFactory.Create();
        return await db.Tasks
            .Where(t => t.Triage == TaskTriage.Inbox && t.ParentTaskId == null && t.Status != TaskItemStatus.Cancelled)
            .OrderByDescending(t => t.CreatedUtc)
            .ToView(db.SprintTasks)
            .ToListAsync(ct);
    }

    public async Task<int> InboxCountAsync(CancellationToken ct = default)
    {
        await using var db = dbFactory.Create();
        return await db.Tasks.CountAsync(
            t => t.Triage == TaskTriage.Inbox && t.ParentTaskId == null && t.Status != TaskItemStatus.Cancelled, ct);
    }

    /// <summary>Open top-level backlog tasks, newest priority first. Includes tasks already planned (see PlannedWeekStart).</summary>
    public async Task<IReadOnlyList<TaskView>> ListBacklogAsync(TaskFilter? filter = null, CancellationToken ct = default)
    {
        await using var db = dbFactory.Create();
        var query = db.Tasks.Where(t => t.Triage == TaskTriage.Backlog && t.ParentTaskId == null
                                        && (t.Status == TaskItemStatus.ToDo || t.Status == TaskItemStatus.InProgress));
        query = ApplyFilter(query, filter);
        return await query
            .OrderByDescending(t => t.Priority).ThenBy(t => t.DueDate == null).ThenBy(t => t.DueDate).ThenBy(t => t.CreatedUtc)
            .ToView(db.SprintTasks)
            .ToListAsync(ct);
    }

    /// <summary>Open backlog tasks that are not planned in any open sprint: candidates for sprint planning.</summary>
    public async Task<IReadOnlyList<TaskView>> ListPlannableAsync(TaskFilter? filter = null, CancellationToken ct = default)
    {
        var backlog = await ListBacklogAsync(filter, ct);
        return backlog.Where(t => t.PlannedWeekStart is null).ToList();
    }

    public async Task<IReadOnlyList<TaskView>> ListForProjectAsync(Guid projectId, CancellationToken ct = default)
    {
        await using var db = dbFactory.Create();
        return await db.Tasks
            .Where(t => t.ProjectId == projectId && t.ParentTaskId == null && t.Status != TaskItemStatus.Cancelled)
            .OrderBy(t => t.Status == TaskItemStatus.Done).ThenByDescending(t => t.Priority).ThenBy(t => t.CreatedUtc)
            .ToView(db.SprintTasks)
            .ToListAsync(ct);
    }

    private static IQueryable<TaskItem> ApplyFilter(IQueryable<TaskItem> query, TaskFilter? filter)
    {
        if (filter is null)
        {
            return query;
        }

        if (filter.LifeAreaId is { } area)
        {
            query = query.Where(t => t.LifeAreaId == area);
        }

        if (filter.ProjectId is { } project)
        {
            query = query.Where(t => t.ProjectId == project);
        }

        if (!string.IsNullOrWhiteSpace(filter.Text))
        {
            var text = filter.Text.Trim();
            query = query.Where(t => EF.Functions.Like(t.Title, "%" + text + "%"));
        }

        return query;
    }

    private static async Task<TaskItem> Load(IAppDbContext db, Guid id, CancellationToken ct) =>
        await db.Tasks.FirstOrDefaultAsync(t => t.Id == id, ct)
        ?? throw new PlanningException(PlanningException.Codes.NotFound);

    private static void Apply(TaskItem task, TaskInput input)
    {
        task.Title = RequireTitle(input.Title);
        task.Notes = string.IsNullOrWhiteSpace(input.Notes) ? null : input.Notes.Trim();
        task.Priority = input.Priority;
        task.EstimateMinutes = ValidateEstimate(input.EstimateMinutes);
        task.DueDate = input.DueDate;
        task.ScheduledDate = input.ScheduledDate;
        task.ScheduledTime = input.ScheduledDate is null ? null : input.ScheduledTime;
        task.LifeAreaId = input.LifeAreaId;
        task.ProjectId = input.ProjectId;
    }

    private static string RequireTitle(string? title)
    {
        title = title?.Trim() ?? string.Empty;
        if (title.Length == 0)
        {
            throw new PlanningException(PlanningException.Codes.TitleRequired);
        }

        return title.Length > MaxTitleLength ? title[..MaxTitleLength] : title;
    }

    private static int? ValidateEstimate(int? minutes)
    {
        if (minutes is null)
        {
            return null;
        }

        if (minutes is <= 0 or > MaxEstimateMinutes)
        {
            throw new PlanningException(PlanningException.Codes.InvalidEstimate);
        }

        return minutes;
    }
}
