using System.Globalization;
using LifeOS.Application.Abstractions;
using LifeOS.Application.Organize;
using LifeOS.Domain.Planning;
using LifeOS.Domain.Tasks;
using LifeOS.Domain.Time;
using Microsoft.EntityFrameworkCore;

namespace LifeOS.Application.Planning;

/// <summary>
/// Weekly sprints. Every scope change is written to an append-only log in the same save as the change,
/// so retrospectives can tell planned work from added work.
/// </summary>
public sealed class SprintService(IAppDbContextFactory dbFactory, IUserClock clock)
{
    public DateOnly CurrentWeekStart => PeriodCalculator.WeekStart(clock.Today);

    public async Task<SprintBoard> GetBoardAsync(DateOnly anyDateInWeek, CancellationToken ct = default)
    {
        var week = PeriodCalculator.WeekStart(anyDateInWeek);
        await using var db = dbFactory.Create();
        var sprint = await db.Sprints.FirstOrDefaultAsync(s => s.WeekStart == week, ct);
        return await BuildBoardAsync(db, week, sprint, ct);
    }

    public async Task AddTaskAsync(DateOnly anyDateInWeek, Guid taskId, CancellationToken ct = default)
    {
        var week = PeriodCalculator.WeekStart(anyDateInWeek);
        await using var db = dbFactory.Create();
        var task = await LoadTask(db, taskId, ct);
        RequirePlannable(task);

        var sprint = await EnsureSprintAsync(db, week, ct);
        RequireOpen(sprint);

        var elsewhere = await db.SprintTasks
            .Where(st => st.TaskItemId == taskId && st.LeftUtc == null && st.Sprint.ClosedUtc == null
                         && st.SprintId != sprint.Id)
            .AnyAsync(ct);
        if (elsewhere)
        {
            throw new PlanningException(PlanningException.Codes.PlannedInOtherSprint);
        }

        await AttachAsync(db, sprint, task, ScopeChangeReason.Planned, relatedSprintId: null, ct);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Moves a planned task to another week's sprint (logged as removed + added, reason Rescheduled).</summary>
    public async Task MoveTaskAsync(Guid taskId, DateOnly toAnyDateInWeek, CancellationToken ct = default)
    {
        var toWeek = PeriodCalculator.WeekStart(toAnyDateInWeek);
        await using var db = dbFactory.Create();
        var task = await LoadTask(db, taskId, ct);
        RequirePlannable(task);

        var target = await EnsureSprintAsync(db, toWeek, ct);
        RequireOpen(target);

        var current = await db.SprintTasks
            .Include(st => st.Sprint)
            .FirstOrDefaultAsync(st => st.TaskItemId == taskId && st.LeftUtc == null && st.Sprint.ClosedUtc == null, ct);

        if (current is not null)
        {
            if (current.SprintId == target.Id)
            {
                throw new PlanningException(PlanningException.Codes.AlreadyInSprint);
            }

            Detach(db, current, task, ScopeChangeReason.Rescheduled, target.Id);
        }

        await AttachAsync(db, target, task, ScopeChangeReason.Rescheduled, current?.SprintId, ct);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Takes a task out of a sprint. It stays in the backlog.</summary>
    public async Task RemoveTaskAsync(DateOnly anyDateInWeek, Guid taskId, CancellationToken ct = default)
    {
        var week = PeriodCalculator.WeekStart(anyDateInWeek);
        await using var db = dbFactory.Create();
        var sprint = await db.Sprints.FirstOrDefaultAsync(s => s.WeekStart == week, ct)
                     ?? throw new PlanningException(PlanningException.Codes.NotFound);
        RequireOpen(sprint);

        var membership = await db.SprintTasks.FirstOrDefaultAsync(
                             st => st.SprintId == sprint.Id && st.TaskItemId == taskId && st.LeftUtc == null, ct)
                         ?? throw new PlanningException(PlanningException.Codes.NotFound);
        var task = await LoadTask(db, taskId, ct);

        Detach(db, membership, task, ScopeChangeReason.Dropped, relatedSprintId: null);
        await db.SaveChangesAsync(ct);
    }

    public async Task ReorderAsync(DateOnly anyDateInWeek, IReadOnlyList<Guid> orderedTaskIds, CancellationToken ct = default)
    {
        var week = PeriodCalculator.WeekStart(anyDateInWeek);
        await using var db = dbFactory.Create();
        var sprint = await db.Sprints.FirstOrDefaultAsync(s => s.WeekStart == week, ct);
        if (sprint is null)
        {
            return;
        }

        var memberships = await db.SprintTasks.Where(st => st.SprintId == sprint.Id && st.LeftUtc == null).ToListAsync(ct);
        var position = 0;
        foreach (var id in orderedTaskIds)
        {
            var m = memberships.FirstOrDefault(x => x.TaskItemId == id);
            if (m is not null)
            {
                m.Position = position++;
            }
        }

        foreach (var m in memberships.Where(m => !orderedTaskIds.Contains(m.TaskItemId)).OrderBy(m => m.Position))
        {
            m.Position = position++;
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task SetGoalAsync(DateOnly anyDateInWeek, string? goal, CancellationToken ct = default)
    {
        await using var db = dbFactory.Create();
        var sprint = await EnsureSprintAsync(db, PeriodCalculator.WeekStart(anyDateInWeek), ct);
        RequireOpen(sprint);
        sprint.Goal = string.IsNullOrWhiteSpace(goal) ? null : goal.Trim();
        await db.SaveChangesAsync(ct);
    }

    public async Task SetCapacityAsync(DateOnly anyDateInWeek, int? capacityMinutes, CancellationToken ct = default)
    {
        if (capacityMinutes is <= 0 or > 100_000)
        {
            throw new PlanningException(PlanningException.Codes.InvalidCapacity);
        }

        await using var db = dbFactory.Create();
        var sprint = await EnsureSprintAsync(db, PeriodCalculator.WeekStart(anyDateInWeek), ct);
        RequireOpen(sprint);
        sprint.CapacityMinutes = capacityMinutes;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Optional explicit start: scope changes after this moment count as added work.</summary>
    public async Task CommitAsync(DateOnly anyDateInWeek, CancellationToken ct = default)
    {
        await using var db = dbFactory.Create();
        var sprint = await EnsureSprintAsync(db, PeriodCalculator.WeekStart(anyDateInWeek), ct);
        RequireOpen(sprint);
        sprint.CommittedUtc ??= clock.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Closes a sprint. Unfinished tasks are carried to the next week's sprint or sent back to the backlog,
    /// per task via <paramref name="overrides"/> or otherwise by <paramref name="defaultDecision"/>.
    /// Finished and cancelled tasks stay in the closed sprint as its history.
    /// </summary>
    public async Task CloseAsync(
        DateOnly anyDateInWeek,
        CarryDecision defaultDecision,
        IReadOnlyDictionary<Guid, CarryDecision>? overrides = null,
        CancellationToken ct = default)
    {
        var week = PeriodCalculator.WeekStart(anyDateInWeek);
        await using var db = dbFactory.Create();
        var sprint = await db.Sprints.FirstOrDefaultAsync(s => s.WeekStart == week, ct)
                     ?? throw new PlanningException(PlanningException.Codes.NotFound);
        if (sprint.IsClosed)
        {
            return;
        }

        var unfinished = await db.SprintTasks
            .Include(st => st.Task)
            .Where(st => st.SprintId == sprint.Id && st.LeftUtc == null
                         && (st.Task.Status == TaskItemStatus.ToDo || st.Task.Status == TaskItemStatus.InProgress))
            .OrderBy(st => st.Position)
            .ToListAsync(ct);

        Sprint? next = null;
        foreach (var membership in unfinished)
        {
            var decision = overrides is not null && overrides.TryGetValue(membership.TaskItemId, out var d) ? d : defaultDecision;
            if (decision == CarryDecision.NextSprint)
            {
                next ??= await EnsureSprintAsync(db, week.AddDays(7), ct);
                RequireOpen(next);
                Detach(db, membership, membership.Task, ScopeChangeReason.CarryOver, next.Id);
                await AttachAsync(db, next, membership.Task, ScopeChangeReason.CarryOver, sprint.Id, ct);
            }
            else
            {
                Detach(db, membership, membership.Task, ScopeChangeReason.ReturnedToBacklog, relatedSprintId: null);
            }
        }

        sprint.ClosedUtc = clock.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Past, still-open sprints that have unfinished tasks. Used for a gentle "close last week?" hint.</summary>
    public async Task<IReadOnlyList<PastSprintToClose>> ListPastSprintsToCloseAsync(CancellationToken ct = default)
    {
        var current = CurrentWeekStart;
        await using var db = dbFactory.Create();
        var rows = await db.Sprints
            .Where(s => s.WeekStart < current && s.ClosedUtc == null)
            .Select(s => new
            {
                s.WeekStart,
                Open = db.SprintTasks.Count(st => st.SprintId == s.Id && st.LeftUtc == null
                                                  && (st.Task.Status == TaskItemStatus.ToDo || st.Task.Status == TaskItemStatus.InProgress)),
            })
            .Where(x => x.Open > 0)
            .OrderBy(x => x.WeekStart)
            .ToListAsync(ct);
        return rows.Select(x => new PastSprintToClose(x.WeekStart, IsoWeek(x.WeekStart), x.Open)).ToList();
    }

    public async Task<IReadOnlyList<ScopeChangeView>> GetScopeLogAsync(DateOnly anyDateInWeek, CancellationToken ct = default)
    {
        var week = PeriodCalculator.WeekStart(anyDateInWeek);
        await using var db = dbFactory.Create();
        var sprint = await db.Sprints.FirstOrDefaultAsync(s => s.WeekStart == week, ct);
        if (sprint is null)
        {
            return [];
        }

        var boundary = BoundaryOf(sprint);
        var changes = await db.ScopeChanges.Where(c => c.SprintId == sprint.Id)
            .OrderBy(c => c.OccurredUtc)
            .ToListAsync(ct);
        return changes
            .Select(c => new ScopeChangeView(c.TaskItemId, c.TaskTitle, c.Kind, c.Reason, c.OccurredUtc, c.OccurredUtc >= boundary))
            .ToList();
    }

    public static int IsoWeek(DateOnly date) => ISOWeek.GetWeekOfYear(date.ToDateTime(TimeOnly.MinValue));

    private DateTimeOffset BoundaryOf(Sprint sprint) =>
        sprint.CommittedUtc ?? PeriodCalculator.LocalMidnightToUtc(sprint.WeekStart, clock.Zone);

    private async Task<SprintBoard> BuildBoardAsync(IAppDbContext db, DateOnly week, Sprint? sprint, CancellationToken ct)
    {
        var boundary = sprint is null ? PeriodCalculator.LocalMidnightToUtc(week, clock.Zone) : BoundaryOf(sprint);
        var isCurrent = week == CurrentWeekStart;

        if (sprint is null)
        {
            return new SprintBoard(week, IsoWeek(week), null, null, null, 0, CapacityStatus.NotSet, isCurrent,
                false, null, boundary, false, [], SprintScopeStats.Empty);
        }

        var members = await db.SprintTasks
            .Where(st => st.SprintId == sprint.Id)
            .OrderBy(st => st.Position)
            .Select(st => new { st.TaskItemId, st.Position, st.LeftUtc, st.LeaveReason, st.AddedUtc })
            .ToListAsync(ct);
        var ids = members.Select(m => m.TaskItemId).ToList();
        var views = await db.Tasks.Where(t => ids.Contains(t.Id)).ToView(db.SprintTasks).ToListAsync(ct);
        var byId = views.ToDictionary(v => v.Id);

        var tasks = members
            .Where(m => byId.ContainsKey(m.TaskItemId))
            .Select(m => new SprintBoardTask(byId[m.TaskItemId], m.Position, m.LeftUtc is null, m.LeaveReason, m.AddedUtc))
            .ToList();

        var planned = tasks
            .Where(t => t.IsActive && t.Task.Status != TaskItemStatus.Cancelled)
            .Sum(t => t.Task.EstimateMinutes ?? 0);

        var changes = await db.ScopeChanges.Where(c => c.SprintId == sprint.Id).ToListAsync(ct);
        var stats = SprintScope.Compute(changes, boundary);

        return new SprintBoard(
            week,
            IsoWeek(week),
            sprint.Id,
            sprint.Goal,
            sprint.CapacityMinutes,
            planned,
            SprintCapacity.Evaluate(planned, sprint.CapacityMinutes),
            isCurrent,
            sprint.CommittedUtc is not null,
            sprint.CommittedUtc,
            boundary,
            sprint.IsClosed,
            tasks,
            stats);
    }

    private async Task<Sprint> EnsureSprintAsync(IAppDbContext db, DateOnly week, CancellationToken ct)
    {
        var sprint = await db.Sprints.FirstOrDefaultAsync(s => s.WeekStart == week, ct);
        if (sprint is null)
        {
            sprint = new Sprint { WeekStart = week };
            db.Sprints.Add(sprint);
        }

        return sprint;
    }

    private async Task AttachAsync(
        IAppDbContext db, Sprint sprint, TaskItem task, ScopeChangeReason reason, Guid? relatedSprintId, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var existing = await db.SprintTasks.FirstOrDefaultAsync(
            st => st.SprintId == sprint.Id && st.TaskItemId == task.Id, ct);

        var lastPosition = await db.SprintTasks
            .Where(st => st.SprintId == sprint.Id && st.LeftUtc == null)
            .MaxAsync(st => (int?)st.Position, ct) ?? -1;

        if (existing is { LeftUtc: null })
        {
            throw new PlanningException(PlanningException.Codes.AlreadyInSprint);
        }

        if (existing is not null)
        {
            existing.LeftUtc = null;
            existing.LeaveReason = null;
            existing.AddedUtc = now;
            existing.Position = lastPosition + 1;
        }
        else
        {
            db.SprintTasks.Add(new SprintTask
            {
                SprintId = sprint.Id,
                TaskItemId = task.Id,
                Position = lastPosition + 1,
                AddedUtc = now,
            });
        }

        task.Triage = TaskTriage.Backlog;
        db.ScopeChanges.Add(NewChange(sprint.Id, task, ScopeChangeKind.Added, reason, now, relatedSprintId));
    }

    private void Detach(IAppDbContext db, SprintTask membership, TaskItem task, ScopeChangeReason reason, Guid? relatedSprintId)
    {
        var now = clock.UtcNow;
        membership.LeftUtc = now;
        membership.LeaveReason = reason;
        db.ScopeChanges.Add(NewChange(membership.SprintId, task, ScopeChangeKind.Removed, reason, now, relatedSprintId));
    }

    private static SprintScopeChange NewChange(
        Guid sprintId, TaskItem task, ScopeChangeKind kind, ScopeChangeReason reason, DateTimeOffset now, Guid? related) =>
        new()
        {
            SprintId = sprintId,
            TaskItemId = task.Id,
            TaskTitle = task.Title,
            EstimateMinutes = task.EstimateMinutes,
            Kind = kind,
            Reason = reason,
            OccurredUtc = now,
            RelatedSprintId = related,
        };

    private static async Task<TaskItem> LoadTask(IAppDbContext db, Guid id, CancellationToken ct) =>
        await db.Tasks.FirstOrDefaultAsync(t => t.Id == id, ct)
        ?? throw new PlanningException(PlanningException.Codes.NotFound);

    private static void RequirePlannable(TaskItem task)
    {
        if (task.ParentTaskId is not null)
        {
            throw new PlanningException(PlanningException.Codes.SubtaskNotPlannable);
        }

        if (!task.IsOpen)
        {
            throw new PlanningException(PlanningException.Codes.TaskNotOpen);
        }
    }

    private static void RequireOpen(Sprint sprint)
    {
        if (sprint.IsClosed)
        {
            throw new PlanningException(PlanningException.Codes.SprintClosed);
        }
    }
}
