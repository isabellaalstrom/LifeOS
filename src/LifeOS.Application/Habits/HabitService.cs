using LifeOS.Application.Abstractions;
using LifeOS.Application.Organize;
using LifeOS.Application.Planning;
using LifeOS.Domain.Habits;
using LifeOS.Domain.Tasks;
using LifeOS.Domain.Time;
using Microsoft.EntityFrameworkCore;

namespace LifeOS.Application.Habits;

public sealed class HabitService(
    IAppDbContextFactory dbFactory,
    IUserClock clock,
    HabitCompletionRecorder recorder,
    TaskService tasks,
    SprintService sprints)
{
    public const int MaxTarget = 100;

    public async Task<IReadOnlyList<HabitView>> ListAsync(bool includeArchived = false, CancellationToken ct = default)
    {
        await using var db = dbFactory.Create();
        var habits = await db.Habits.Include(h => h.LifeArea).Include(h => h.Goal)
            .Where(h => includeArchived || !h.IsArchived)
            .OrderBy(h => h.IsArchived).ThenBy(h => h.Name)
            .ToListAsync(ct);
        return await BuildViewsAsync(db, habits, ct);
    }

    public async Task<HabitDetail?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = dbFactory.Create();
        var habit = await db.Habits.Include(h => h.LifeArea).Include(h => h.Goal).FirstOrDefaultAsync(h => h.Id == id, ct);
        if (habit is null)
        {
            return null;
        }

        var view = (await BuildViewsAsync(db, [habit], ct))[0];
        var all = await db.HabitCompletions.Where(c => c.HabitId == id)
            .OrderByDescending(c => c.Date).ThenByDescending(c => c.CompletedUtc)
            .ToListAsync(ct);
        var dates = all.Select(c => c.Date).ToList();

        var stats = new List<HabitPeriodStat>();
        var cursor = HabitRules.PeriodStart(habit.Period, clock.Today);
        for (var i = 0; i < 8; i++)
        {
            stats.Add(new HabitPeriodStat(cursor, HabitRules.CountInPeriod(dates, habit.Period, cursor), habit.TargetCount));
            cursor = HabitRules.Previous(habit.Period, cursor);
        }

        stats.Reverse();
        var completions = all.Take(30).Select(c => new HabitCompletionView(c.Id, c.Date, c.CompletedUtc, c.Note, c.TaskItemId is not null)).ToList();
        return new HabitDetail(view, stats, completions);
    }

    public async Task<Guid> CreateAsync(HabitInput input, CancellationToken ct = default)
    {
        await using var db = dbFactory.Create();
        var habit = new Habit();
        Apply(habit, input);
        db.Habits.Add(habit);
        await db.SaveChangesAsync(ct);
        return habit.Id;
    }

    public async Task UpdateAsync(Guid id, HabitInput input, CancellationToken ct = default)
    {
        await using var db = dbFactory.Create();
        var habit = await Load(db, id, ct);
        Apply(habit, input);
        await db.SaveChangesAsync(ct);
    }

    public async Task SetArchivedAsync(Guid id, bool archived, CancellationToken ct = default)
    {
        await using var db = dbFactory.Create();
        var habit = await Load(db, id, ct);
        habit.IsArchived = archived;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Logs one completion (today by default). If the habit has an open generated sprint task in that period, that task is
    /// finished instead, so one occurrence is never counted twice.
    /// </summary>
    public async Task LogAsync(Guid habitId, DateOnly? date = null, string? note = null, CancellationToken ct = default)
    {
        var day = date ?? clock.Today;
        if (day > clock.Today)
        {
            throw new PlanningException(PlanningException.Codes.FutureDate);
        }

        Guid? taskToFinish = null;
        await using (var db = dbFactory.Create())
        {
            var habit = await Load(db, habitId, ct);
            if (habit is { Period: HabitPeriod.Week, GenerateSprintTasks: true })
            {
                var week = HabitRules.PeriodStart(HabitPeriod.Week, day);
                taskToFinish = await db.Tasks
                    .Where(t => t.HabitId == habitId && t.GeneratedForPeriodStart == week
                                && (t.Status == TaskItemStatus.ToDo || t.Status == TaskItemStatus.InProgress))
                    .OrderBy(t => t.GeneratedIndex)
                    .Select(t => (Guid?)t.Id)
                    .FirstOrDefaultAsync(ct);
            }

            if (taskToFinish is null)
            {
                await using var tx = await db.BeginTransactionAsync(ct);
                await recorder.AddAsync(db, habit, day, null, note, ct);
                await tx.CommitAsync(ct);
                return;
            }
        }

        await tasks.CompleteAsync(taskToFinish.Value, day, ct);
    }

    /// <summary>Removes the latest completion on a day. If it came from a generated task, that task is reopened.</summary>
    public async Task UnlogAsync(Guid habitId, DateOnly date, CancellationToken ct = default)
    {
        Guid? taskToReopen = null;
        await using (var db = dbFactory.Create())
        {
            var habit = await Load(db, habitId, ct);
            var completion = await db.HabitCompletions
                                 .Where(c => c.HabitId == habitId && c.Date == date)
                                 .OrderByDescending(c => c.CompletedUtc)
                                 .FirstOrDefaultAsync(ct)
                             ?? throw new PlanningException(PlanningException.Codes.NotFound);

            if (completion.TaskItemId is { } taskId)
            {
                taskToReopen = taskId;
            }
            else
            {
                await using var tx = await db.BeginTransactionAsync(ct);
                await recorder.RemoveAsync(db, habit, completion, ct);
                await tx.CommitAsync(ct);
                return;
            }
        }

        await tasks.SetStatusAsync(taskToReopen.Value, TaskItemStatus.ToDo, ct);
    }

    /// <summary>
    /// Creates this week's sprint tasks for weekly habits that opted in, and plans them into the week's sprint.
    /// Safe to run repeatedly: a unique key (habit, week, index) stops duplicates, and completions already logged this
    /// week reduce how many tasks are created.
    /// </summary>
    public async Task<GenerateHabitTasksResult> GenerateWeekTasksAsync(DateOnly anyDateInWeek, CancellationToken ct = default)
    {
        var week = PeriodCalculator.WeekStart(anyDateInWeek);
        var board = await sprints.GetBoardAsync(week, ct);
        if (board.IsClosed)
        {
            throw new PlanningException(PlanningException.Codes.SprintClosed);
        }

        var created = 0;
        await using (var db = dbFactory.Create())
        {
            var habits = await db.Habits
                .Where(h => !h.IsArchived && h.Period == HabitPeriod.Week && h.GenerateSprintTasks)
                .ToListAsync(ct);
            var weekEnd = week.AddDays(7);

            foreach (var habit in habits)
            {
                var existing = await db.Tasks
                    .Where(t => t.HabitId == habit.Id && t.GeneratedForPeriodStart == week)
                    .Select(t => t.GeneratedIndex ?? 0)
                    .ToListAsync(ct);
                var manual = await db.HabitCompletions
                    .CountAsync(c => c.HabitId == habit.Id && c.TaskItemId == null && c.Date >= week && c.Date < weekEnd, ct);
                var toCreate = Math.Max(0, habit.TargetCount - manual - existing.Count);
                var nextIndex = (existing.Count == 0 ? 0 : existing.Max()) + 1;

                for (var i = 0; i < toCreate; i++, nextIndex++)
                {
                    db.Tasks.Add(new TaskItem
                    {
                        Title = $"{habit.Name} ({nextIndex}/{habit.TargetCount})",
                        Triage = TaskTriage.Backlog,
                        EstimateMinutes = habit.EstimateMinutes,
                        LifeAreaId = habit.LifeAreaId,
                        GoalId = habit.GoalId,
                        HabitId = habit.Id,
                        GeneratedForPeriodStart = week,
                        GeneratedIndex = nextIndex,
                    });
                    created++;
                }
            }

            await db.SaveChangesAsync(ct);
        }

        // Plan every open generated task of the week that is not in a sprint yet (also repairs an interrupted earlier run).
        var planned = 0;
        List<Guid> unplanned;
        await using (var db = dbFactory.Create())
        {
            unplanned = await db.Tasks
                .Where(t => t.HabitId != null && t.GeneratedForPeriodStart == week
                            && (t.Status == TaskItemStatus.ToDo || t.Status == TaskItemStatus.InProgress))
                .OrderBy(t => t.GeneratedIndex)
                .Select(t => t.Id)
                .ToListAsync(ct);
        }

        foreach (var id in unplanned)
        {
            try
            {
                await sprints.AddTaskAsync(week, id, ct);
                planned++;
            }
            catch (PlanningException ex) when (ex.Code is PlanningException.Codes.AlreadyInSprint or PlanningException.Codes.PlannedInOtherSprint)
            {
                // Already planned: nothing to do.
            }
        }

        return new GenerateHabitTasksResult(created, planned);
    }

    private async Task<IReadOnlyList<HabitView>> BuildViewsAsync(IAppDbContext db, List<Habit> habits, CancellationToken ct)
    {
        var ids = habits.Select(h => h.Id).ToList();
        var today = clock.Today;
        var weekStart = HabitRules.PeriodStart(HabitPeriod.Week, today);

        var completions = await db.HabitCompletions.Where(c => ids.Contains(c.HabitId))
            .Select(c => new { c.HabitId, c.Date })
            .ToListAsync(ct);
        var openTasks = await db.Tasks
            .Where(t => t.HabitId != null && ids.Contains(t.HabitId.Value) && t.GeneratedForPeriodStart == weekStart
                        && (t.Status == TaskItemStatus.ToDo || t.Status == TaskItemStatus.InProgress))
            .OrderBy(t => t.GeneratedIndex)
            .Select(t => new { HabitId = t.HabitId!.Value, t.Id })
            .ToListAsync(ct);

        return habits.Select(h =>
        {
            var dates = completions.Where(c => c.HabitId == h.Id).Select(c => c.Date).ToList();
            var start = HabitRules.PeriodStart(h.Period, today);
            var count = HabitRules.CountInPeriod(dates, h.Period, start);
            return new HabitView(
                h.Id, h.Name, h.Notes, h.Period, h.TargetCount, h.LifeAreaId, h.LifeArea?.Name, h.GoalId, h.Goal?.Title,
                h.GoalIncrement, h.EstimateMinutes, h.GenerateSprintTasks, h.IsArchived, start, count, count >= h.TargetCount,
                HabitRules.Streak(dates, h.Period, h.TargetCount, today),
                openTasks.FirstOrDefault(t => t.HabitId == h.Id)?.Id,
                dates.Count(d => d == today));
        }).ToList();
    }

    private static void Apply(Habit habit, HabitInput input)
    {
        var name = input.Name?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            throw new PlanningException(PlanningException.Codes.NameRequired);
        }

        if (input.TargetCount is < 1 or > MaxTarget)
        {
            throw new PlanningException(PlanningException.Codes.InvalidTarget);
        }

        if (input.EstimateMinutes is <= 0 or > TaskService.MaxEstimateMinutes)
        {
            throw new PlanningException(PlanningException.Codes.InvalidEstimate);
        }

        habit.Name = name.Length > 150 ? name[..150] : name;
        habit.Notes = string.IsNullOrWhiteSpace(input.Notes) ? null : input.Notes.Trim();
        habit.Period = input.Period;
        habit.TargetCount = input.TargetCount;
        habit.LifeAreaId = input.LifeAreaId;
        habit.GoalId = input.GoalId;
        habit.GoalIncrement = input.GoalIncrement;
        habit.EstimateMinutes = input.EstimateMinutes;
        // Only weekly habits can generate tasks: the sprint is a week.
        habit.GenerateSprintTasks = input.GenerateSprintTasks && input.Period == HabitPeriod.Week;
    }

    private static async Task<Habit> Load(IAppDbContext db, Guid id, CancellationToken ct) =>
        await db.Habits.FirstOrDefaultAsync(h => h.Id == id, ct)
        ?? throw new PlanningException(PlanningException.Codes.NotFound);
}
