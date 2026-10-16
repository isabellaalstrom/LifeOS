using LifeOS.Application.Abstractions;
using LifeOS.Application.Habits;
using LifeOS.Application.Organize;
using LifeOS.Application.Planning;
using LifeOS.Domain.Goals;
using LifeOS.Domain.Habits;
using LifeOS.Domain.Tasks;
using Microsoft.EntityFrameworkCore;

namespace LifeOS.Application.Goals;

public sealed class GoalService(IAppDbContextFactory dbFactory, IUserClock clock, GoalProgressRecorder recorder)
{
    public async Task<IReadOnlyList<GoalView>> ListAsync(
        GoalHorizon? horizon = null, DateOnly? periodStart = null, bool includeClosed = false, CancellationToken ct = default)
    {
        await using var db = dbFactory.Create();
        var query = db.Goals.Include(g => g.LifeArea).AsQueryable();
        if (horizon is { } h)
        {
            query = query.Where(g => g.Horizon == h);
        }

        if (periodStart is { } p)
        {
            query = query.Where(g => g.PeriodStart == p);
        }

        if (!includeClosed)
        {
            query = query.Where(g => g.Status == GoalStatus.Active || g.Status == GoalStatus.Paused);
        }

        var goals = await query.OrderBy(EnumOrdering.HorizonRank).ThenBy(g => g.PeriodStart).ThenBy(g => g.CreatedUtc).ToListAsync(ct);
        return await BuildViewsAsync(db, goals, ct);
    }

    /// <summary>Active goals whose period contains today, for the overview.</summary>
    public async Task<IReadOnlyList<GoalView>> ListCurrentAsync(int take = 5, CancellationToken ct = default)
    {
        var today = clock.Today;
        await using var db = dbFactory.Create();
        var goals = await db.Goals.Include(g => g.LifeArea)
            .Where(g => g.Status == GoalStatus.Active)
            .OrderBy(EnumOrdering.HorizonRank).ThenBy(g => g.CreatedUtc)
            .ToListAsync(ct);
        goals = goals.Where(g => GoalPeriods.ContainsDate(g.Horizon, g.PeriodStart, today)).Take(take).ToList();
        return await BuildViewsAsync(db, goals, ct);
    }

    public async Task<GoalDetail?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = dbFactory.Create();
        var goal = await db.Goals.Include(g => g.LifeArea).FirstOrDefaultAsync(g => g.Id == id, ct);
        if (goal is null)
        {
            return null;
        }

        var view = (await BuildViewsAsync(db, [goal], ct))[0];
        var milestones = await db.GoalMilestones.Where(m => m.GoalId == id)
            .OrderBy(m => m.SortOrder).ThenBy(m => m.CreatedUtc)
            .Select(m => new MilestoneView(m.Id, m.Title, m.DueDate, m.CompletedUtc != null, m.SortOrder))
            .ToListAsync(ct);
        var history = await db.GoalProgressEntries.Where(e => e.GoalId == id)
            .OrderBy(e => e.RecordedUtc)
            .Select(e => new GoalHistoryPoint(e.RecordedUtc, e.Percent, e.Value, e.Target, e.Source, e.Note))
            .ToListAsync(ct);
        var children = await BuildViewsAsync(
            db, await db.Goals.Include(g => g.LifeArea).Where(g => g.ParentGoalId == id).OrderBy(g => g.PeriodStart).ToListAsync(ct), ct);
        var tasks = await db.Tasks
            .Where(t => t.ParentTaskId == null && t.Status != TaskItemStatus.Cancelled
                        && (t.GoalId == id || (t.Project != null && t.Project.GoalId == id)))
            .OrderBy(t => t.Status == TaskItemStatus.Done).ThenBy(t => t.CreatedUtc)
            .ToView(db.SprintTasks)
            .ToListAsync(ct);
        var projects = await db.Projects.Where(p => p.GoalId == id)
            .Select(p => new ProjectView(
                p.Id, p.Name, p.Description, p.LifeAreaId, p.LifeArea != null ? p.LifeArea.Name : null, p.Status,
                db.Tasks.Count(t => t.ProjectId == p.Id && t.ParentTaskId == null && (t.Status == TaskItemStatus.ToDo || t.Status == TaskItemStatus.InProgress)),
                db.Tasks.Count(t => t.ProjectId == p.Id && t.ParentTaskId == null && t.Status == TaskItemStatus.Done),
                p.GoalId, p.Goal != null ? p.Goal.Title : null))
            .ToListAsync(ct);
        var habits = await db.Habits.Where(h => h.GoalId == id && !h.IsArchived)
            .OrderBy(h => h.Name)
            .Select(h => new HabitView(
                h.Id, h.Name, h.Notes, h.Period, h.TargetCount, h.LifeAreaId, h.LifeArea != null ? h.LifeArea.Name : null,
                h.GoalId, h.Goal != null ? h.Goal.Title : null, h.GoalIncrement, h.EstimateMinutes, h.GenerateSprintTasks, h.IsArchived,
                clock.Today, 0, false, 0, null, 0))
            .ToListAsync(ct);

        return new GoalDetail(view, milestones, history, children, tasks, projects, habits);
    }

    public async Task<Guid> CreateAsync(GoalInput input, CancellationToken ct = default)
    {
        await using var db = dbFactory.Create();
        await using var tx = await db.BeginTransactionAsync(ct);
        var goal = new Goal();
        await ApplyAsync(db, goal, input, ct);
        db.Goals.Add(goal);
        await db.SaveChangesAsync(ct);

        await recorder.RecordAsync(db, goal.Id, GoalProgressSource.Created, force: true, ct: ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return goal.Id;
    }

    public async Task UpdateAsync(Guid id, GoalInput input, CancellationToken ct = default)
    {
        await using var db = dbFactory.Create();
        await using var tx = await db.BeginTransactionAsync(ct);
        var goal = await Load(db, id, ct);
        var oldTarget = goal.TargetValue;
        var oldMode = goal.Mode;
        var oldCurrent = goal.CurrentValue;

        await ApplyAsync(db, goal, input, ct, id);
        await db.SaveChangesAsync(ct);

        var targetChanged = goal.Mode == GoalProgressMode.Numeric && (oldMode != goal.Mode || oldTarget != goal.TargetValue);
        var source = targetChanged ? GoalProgressSource.TargetChanged : GoalProgressSource.Manual;
        await recorder.RecordAsync(db, id, source, force: targetChanged, ct: ct);
        _ = oldCurrent;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task SetStatusAsync(Guid id, GoalStatus status, CancellationToken ct = default)
    {
        await using var db = dbFactory.Create();
        var goal = await Load(db, id, ct);
        goal.Status = status;
        goal.AchievedUtc = status == GoalStatus.Achieved ? clock.UtcNow : null;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Records a new current value for a numeric goal (with an optional note).</summary>
    public async Task SetNumericValueAsync(Guid id, decimal value, string? note = null, CancellationToken ct = default)
    {
        await using var db = dbFactory.Create();
        await using var tx = await db.BeginTransactionAsync(ct);
        var goal = await Load(db, id, ct);
        if (goal.Mode != GoalProgressMode.Numeric)
        {
            throw new PlanningException(PlanningException.Codes.ModeMismatch);
        }

        goal.CurrentValue = value;
        await db.SaveChangesAsync(ct);
        await recorder.RecordAsync(db, id, GoalProgressSource.Manual, note, force: true, ct: ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task SetManualPercentAsync(Guid id, decimal percent, string? note = null, CancellationToken ct = default)
    {
        if (percent is < 0 or > 100)
        {
            throw new PlanningException(PlanningException.Codes.InvalidPercent);
        }

        await using var db = dbFactory.Create();
        await using var tx = await db.BeginTransactionAsync(ct);
        var goal = await Load(db, id, ct);
        if (goal.Mode != GoalProgressMode.Manual)
        {
            throw new PlanningException(PlanningException.Codes.ModeMismatch);
        }

        goal.ManualPercent = percent;
        await db.SaveChangesAsync(ct);
        await recorder.RecordAsync(db, id, GoalProgressSource.Manual, note, ct: ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task<Guid> AddMilestoneAsync(Guid goalId, string title, DateOnly? dueDate = null, CancellationToken ct = default)
    {
        title = RequireTitle(title);
        await using var db = dbFactory.Create();
        await using var tx = await db.BeginTransactionAsync(ct);
        await Load(db, goalId, ct);
        var next = (await db.GoalMilestones.Where(m => m.GoalId == goalId).MaxAsync(m => (int?)m.SortOrder, ct) ?? -1) + 1;
        var milestone = new GoalMilestone { GoalId = goalId, Title = title, DueDate = dueDate, SortOrder = next };
        db.GoalMilestones.Add(milestone);
        await db.SaveChangesAsync(ct);
        await RecordMilestoneChangeAsync(db, goalId, ct);
        await tx.CommitAsync(ct);
        return milestone.Id;
    }

    public async Task SetMilestoneDoneAsync(Guid milestoneId, bool done, CancellationToken ct = default)
    {
        await using var db = dbFactory.Create();
        await using var tx = await db.BeginTransactionAsync(ct);
        var milestone = await db.GoalMilestones.FirstOrDefaultAsync(m => m.Id == milestoneId, ct)
                        ?? throw new PlanningException(PlanningException.Codes.NotFound);
        if (milestone.IsDone == done)
        {
            return;
        }

        milestone.CompletedUtc = done ? clock.UtcNow : null;
        await db.SaveChangesAsync(ct);
        await RecordMilestoneChangeAsync(db, milestone.GoalId, ct, milestone.Title);
        await tx.CommitAsync(ct);
    }

    public async Task DeleteMilestoneAsync(Guid milestoneId, CancellationToken ct = default)
    {
        await using var db = dbFactory.Create();
        await using var tx = await db.BeginTransactionAsync(ct);
        var milestone = await db.GoalMilestones.FirstOrDefaultAsync(m => m.Id == milestoneId, ct)
                        ?? throw new PlanningException(PlanningException.Codes.NotFound);
        var goalId = milestone.GoalId;
        db.GoalMilestones.Remove(milestone);
        await db.SaveChangesAsync(ct);
        await RecordMilestoneChangeAsync(db, goalId, ct);
        await tx.CommitAsync(ct);
    }

    /// <summary>Removes a goal and its milestones and history. Links from tasks, projects and habits are cleared. Refused when it has child goals.</summary>
    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = dbFactory.Create();
        await using var tx = await db.BeginTransactionAsync(ct);
        var goal = await Load(db, id, ct);
        if (await db.Goals.AnyAsync(g => g.ParentGoalId == id, ct))
        {
            throw new PlanningException(PlanningException.Codes.HasChildren);
        }

        foreach (var t in await db.Tasks.Where(t => t.GoalId == id).ToListAsync(ct))
        {
            t.GoalId = null;
        }

        foreach (var p in await db.Projects.Where(p => p.GoalId == id).ToListAsync(ct))
        {
            p.GoalId = null;
        }

        foreach (var h in await db.Habits.Where(h => h.GoalId == id).ToListAsync(ct))
        {
            h.GoalId = null;
        }

        db.GoalMilestones.RemoveRange(await db.GoalMilestones.Where(m => m.GoalId == id).ToListAsync(ct));
        db.GoalProgressEntries.RemoveRange(await db.GoalProgressEntries.Where(e => e.GoalId == id).ToListAsync(ct));
        await db.SaveChangesAsync(ct);
        db.Goals.Remove(goal);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private async Task RecordMilestoneChangeAsync(IAppDbContext db, Guid goalId, CancellationToken ct, string? note = null)
    {
        var mode = await db.Goals.Where(g => g.Id == goalId).Select(g => g.Mode).FirstAsync(ct);
        if (mode == GoalProgressMode.Milestones)
        {
            await recorder.RecordAsync(db, goalId, GoalProgressSource.Milestone, note, ct: ct);
            await db.SaveChangesAsync(ct);
        }
    }

    private async Task ApplyAsync(IAppDbContext db, Goal goal, GoalInput input, CancellationToken ct, Guid? selfId = null)
    {
        goal.Title = RequireTitle(input.Title);
        goal.Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim();
        goal.Horizon = input.Horizon;
        goal.PeriodStart = GoalPeriods.Start(input.Horizon, input.PeriodStart);
        goal.LifeAreaId = input.LifeAreaId;
        goal.Mode = input.Mode;

        if (input.Mode == GoalProgressMode.Numeric)
        {
            if (input.StartValue is null || input.TargetValue is null)
            {
                throw new PlanningException(PlanningException.Codes.InvalidNumeric);
            }

            goal.StartValue = input.StartValue;
            goal.TargetValue = input.TargetValue;
            goal.CurrentValue = input.CurrentValue ?? goal.CurrentValue ?? input.StartValue;
            goal.Unit = string.IsNullOrWhiteSpace(input.Unit) ? null : input.Unit.Trim();
        }
        else
        {
            goal.StartValue = goal.TargetValue = goal.CurrentValue = null;
            goal.Unit = null;
        }

        goal.ParentGoalId = null;
        if (input.ParentGoalId is { } parentId)
        {
            if (parentId == selfId)
            {
                throw new PlanningException(PlanningException.Codes.InvalidParent);
            }

            var parent = await db.Goals.FirstOrDefaultAsync(g => g.Id == parentId, ct)
                         ?? throw new PlanningException(PlanningException.Codes.InvalidParent);
            if (!GoalPeriods.IsValidParentHorizon(parent.Horizon, goal.Horizon)
                || !GoalPeriods.IsInside(parent.Horizon, parent.PeriodStart, goal.Horizon, goal.PeriodStart))
            {
                throw new PlanningException(PlanningException.Codes.InvalidParent);
            }

            goal.ParentGoalId = parentId;
        }

        // A goal that already has children must stay a longer horizon than all of them.
        if (selfId is { } id)
        {
            var children = await db.Goals.Where(g => g.ParentGoalId == id).ToListAsync(ct);
            if (children.Any(c => !GoalPeriods.IsValidParentHorizon(goal.Horizon, c.Horizon)
                                  || !GoalPeriods.IsInside(goal.Horizon, goal.PeriodStart, c.Horizon, c.PeriodStart)))
            {
                throw new PlanningException(PlanningException.Codes.InvalidParent);
            }
        }
    }

    private static async Task<IReadOnlyList<GoalView>> BuildViewsAsync(IAppDbContext db, List<Goal> goals, CancellationToken ct)
    {
        var counts = await GoalProgressRecorder.LoadCountsAsync(db, goals.Select(g => g.Id).ToList(), ct);
        return goals.Select(g =>
        {
            var c = counts[g.Id];
            var percent = GoalProgressCalculator.ForGoal(g, c.MilestonesDone, c.MilestonesTotal, c.TasksDone, c.TasksTotal);
            var beyond = g is { Mode: GoalProgressMode.Numeric, StartValue: { } s, TargetValue: { } t }
                         && GoalProgressCalculator.IsBeyondTarget(s, t, g.CurrentValue ?? s);
            return new GoalView(
                g.Id, g.Title, g.Description, g.Horizon, g.PeriodStart, g.LifeAreaId, g.LifeArea?.Name, g.ParentGoalId,
                g.Status, g.Mode, Math.Round(percent, 1), g.StartValue, g.TargetValue, g.CurrentValue, g.Unit, g.ManualPercent,
                c.MilestonesDone, c.MilestonesTotal, c.TasksDone, c.TasksTotal, beyond);
        }).ToList();
    }

    private static async Task<Goal> Load(IAppDbContext db, Guid id, CancellationToken ct) =>
        await db.Goals.FirstOrDefaultAsync(g => g.Id == id, ct)
        ?? throw new PlanningException(PlanningException.Codes.NotFound);

    private static string RequireTitle(string? title)
    {
        title = title?.Trim() ?? string.Empty;
        if (title.Length == 0)
        {
            throw new PlanningException(PlanningException.Codes.TitleRequired);
        }

        return title.Length > 200 ? title[..200] : title;
    }
}
