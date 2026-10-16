using LifeOS.Application.Abstractions;
using LifeOS.Application.Planning;
using LifeOS.Domain.Goals;
using LifeOS.Domain.Habits;
using LifeOS.Domain.Retros;
using LifeOS.Domain.Tasks;
using LifeOS.Domain.Time;
using Microsoft.EntityFrameworkCore;

namespace LifeOS.Application.Retros;

/// <summary>
/// Period statistics for retrospectives. Boundaries are calendar periods in the owner's time zone (weeks start on Monday),
/// so a task finished at 23:30 on the last evening of the month belongs to that month even though it is already the next
/// day in UTC. Numbers describe what happened; missing data stays missing rather than counting as failure.
/// </summary>
public sealed class RetroStatsService(IAppDbContextFactory dbFactory, IUserClock clock, SprintService sprints)
{
    public async Task<RetroStats> ComputeAsync(RetroType type, DateOnly anyDateInPeriod, CancellationToken ct = default)
    {
        var start = RetroPeriods.Start(type, anyDateInPeriod);
        var end = RetroPeriods.EndExclusive(type, start);
        var today = clock.Today;
        var (startUtc, endUtc) = PeriodCalculator.ToUtcRange(start, end, clock.Zone);

        await using var db = dbFactory.Create();

        var completed = await db.Tasks
            .Where(t => t.Status == TaskItemStatus.Done && t.ParentTaskId == null
                        && t.CompletedUtc != null && t.CompletedUtc >= startUtc && t.CompletedUtc < endUtc)
            .Select(t => t.EstimateMinutes)
            .ToListAsync(ct);

        return new RetroStats(
            start,
            end,
            InProgress: today < end,
            TasksCompleted: completed.Count,
            TasksCompletedMinutes: completed.Sum(m => m ?? 0),
            Sprints: await SprintStatsAsync(db, start, end, ct),
            Goals: await GoalChangesAsync(db, startUtc, endUtc, ct),
            Habits: await HabitStatsAsync(db, start, end, startUtc, endUtc, today, ct));
    }

    private async Task<RetroSprintStats> SprintStatsAsync(IAppDbContext db, DateOnly start, DateOnly end, CancellationToken ct)
    {
        // A sprint belongs to the period its Monday falls in.
        var weeks = await db.Sprints.Where(s => s.WeekStart >= start && s.WeekStart < end)
            .OrderBy(s => s.WeekStart).Select(s => s.WeekStart).ToListAsync(ct);

        int planned = 0, added = 0, removed = 0, carriedOut = 0, carriedIn = 0, done = 0;
        foreach (var week in weeks)
        {
            var board = await sprints.GetBoardAsync(week, ct);
            planned += board.Stats.Original - board.Stats.OriginalCarriedIn;
            added += board.Stats.AddedAfterStart;
            removed += board.Stats.RemovedAfterStart;
            carriedOut += board.Stats.CarriedOut;
            carriedIn += board.Stats.CarriedIn;
            done += board.ActiveTasks.Count(t => t.Task.Status == TaskItemStatus.Done);
        }

        return new RetroSprintStats(weeks.Count, planned, added, removed, carriedOut, carriedIn, done);
    }

    private static async Task<IReadOnlyList<RetroGoalChange>> GoalChangesAsync(
        IAppDbContext db, DateTimeOffset startUtc, DateTimeOffset endUtc, CancellationToken ct)
    {
        var active = await db.GoalProgressEntries
            .Where(e => e.RecordedUtc >= startUtc && e.RecordedUtc < endUtc)
            .Select(e => e.GoalId).Distinct().ToListAsync(ct);
        var achieved = await db.Goals
            .Where(g => g.AchievedUtc != null && g.AchievedUtc >= startUtc && g.AchievedUtc < endUtc)
            .Select(g => g.Id).ToListAsync(ct);
        var ids = active.Union(achieved).ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        var goals = await db.Goals.Where(g => ids.Contains(g.Id)).OrderBy(g => g.Title).ToListAsync(ct);
        var entries = await db.GoalProgressEntries.Where(e => ids.Contains(e.GoalId) && e.RecordedUtc < endUtc)
            .OrderBy(e => e.RecordedUtc).Select(e => new { e.GoalId, e.RecordedUtc, e.Percent }).ToListAsync(ct);

        return goals.Select(g =>
        {
            var own = entries.Where(e => e.GoalId == g.Id).ToList();
            var before = own.LastOrDefault(e => e.RecordedUtc < startUtc);
            var last = own.LastOrDefault();
            return new RetroGoalChange(
                g.Id, g.Title, before?.Percent, last?.Percent ?? 0m,
                g.AchievedUtc is { } a && a >= startUtc && a < endUtc);
        }).ToList();
    }

    private async Task<IReadOnlyList<RetroHabitStat>> HabitStatsAsync(
        IAppDbContext db, DateOnly start, DateOnly end, DateTimeOffset startUtc, DateTimeOffset endUtc, DateOnly today, CancellationToken ct)
    {
        var completions = await db.HabitCompletions.Where(c => c.Date >= start && c.Date < end)
            .Select(c => new { c.HabitId, c.Date }).ToListAsync(ct);
        var habits = await db.Habits.Where(h => h.CreatedUtc < endUtc).OrderBy(h => h.Name).ToListAsync(ct);

        var result = new List<RetroHabitStat>();
        foreach (var habit in habits)
        {
            var dates = completions.Where(c => c.HabitId == habit.Id).Select(c => c.Date).ToList();
            if (habit.IsArchived && dates.Count == 0)
            {
                continue;
            }

            var createdLocal = PeriodCalculator.LocalDate(habit.CreatedUtc, clock.Zone);

            // Count only periods that lie wholly inside the retro period, have fully passed, and began after the
            // habit was created (a habit is not "missing" a week it did not exist for).
            var cursor = HabitRules.PeriodStart(habit.Period, start);
            if (cursor < start)
            {
                cursor = HabitRules.Next(habit.Period, cursor);
            }

            int met = 0, total = 0;
            while (HabitRules.Next(habit.Period, cursor) <= end && HabitRules.Next(habit.Period, cursor) <= today.AddDays(1))
            {
                if (cursor >= createdLocal)
                {
                    total++;
                    if (HabitRules.CountInPeriod(dates, habit.Period, cursor) >= habit.TargetCount)
                    {
                        met++;
                    }
                }

                cursor = HabitRules.Next(habit.Period, cursor);
            }

            result.Add(new RetroHabitStat(habit.Id, habit.Name, met, total, dates.Count));
        }

        return result;
    }
}
