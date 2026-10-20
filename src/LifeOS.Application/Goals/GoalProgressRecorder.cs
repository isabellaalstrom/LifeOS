using LifeOS.Application.Abstractions;
using LifeOS.Domain.Goals;
using LifeOS.Domain.Tasks;
using Microsoft.EntityFrameworkCore;

namespace LifeOS.Application.Goals;

/// <summary>
/// The single place that works out goal progress and appends history. Every path that can change progress
/// (manual update, milestone, task, habit, link, target change) goes through here, so the history is consistent.
/// Callers save their own change first, then call <see cref="RecordAsync"/>, then save again, inside one transaction.
/// </summary>
public sealed class GoalProgressRecorder(IUserClock clock)
{
    public static async Task<Dictionary<Guid, GoalCounts>> LoadCountsAsync(
        IAppDbContext db, IReadOnlyCollection<Guid> goalIds, CancellationToken ct)
    {
        var result = goalIds.ToDictionary(id => id, _ => GoalCounts.Empty);
        if (goalIds.Count == 0)
        {
            return result;
        }

        var milestones = await db.GoalMilestones
            .Where(m => goalIds.Contains(m.GoalId))
            .Select(m => new { m.GoalId, Done = m.CompletedUtc != null })
            .ToListAsync(ct);

        // A task counts for the goal it is linked to. Subtasks never count (the parent does).
        var tasks = await db.Tasks
            .Where(t => t.ParentTaskId == null && t.Status != TaskItemStatus.Cancelled
                        && t.GoalId != null && goalIds.Contains(t.GoalId.Value))
            .Select(t => new { t.GoalId, Done = t.Status == TaskItemStatus.Done })
            .ToListAsync(ct);

        foreach (var id in goalIds)
        {
            var ms = milestones.Where(m => m.GoalId == id).ToList();
            var ts = tasks.Where(t => t.GoalId == id).ToList();
            result[id] = new GoalCounts(ms.Count(m => m.Done), ms.Count, ts.Count(t => t.Done), ts.Count);
        }

        return result;
    }

    /// <summary>Appends a history entry for the goal unless nothing changed since the last one (or <paramref name="force"/>). Does not save.</summary>
    public async Task RecordAsync(
        IAppDbContext db, Guid goalId, GoalProgressSource source, string? note = null, bool force = false, CancellationToken ct = default)
    {
        var goal = await db.Goals.FirstOrDefaultAsync(g => g.Id == goalId, ct);
        if (goal is null)
        {
            return;
        }

        var counts = (await LoadCountsAsync(db, [goalId], ct))[goalId];
        var percent = Math.Round(GoalProgressCalculator.ForGoal(goal, counts.MilestonesDone, counts.MilestonesTotal, counts.TasksDone, counts.TasksTotal), 2);
        var value = goal.Mode == GoalProgressMode.Numeric ? goal.CurrentValue : null;
        var target = goal.Mode == GoalProgressMode.Numeric ? goal.TargetValue : null;

        if (!force)
        {
            var last = await db.GoalProgressEntries
                .Where(e => e.GoalId == goalId)
                .OrderByDescending(e => e.RecordedUtc)
                .FirstOrDefaultAsync(ct);
            if (last is not null && last.Percent == percent && last.Value == value && last.Target == target)
            {
                return;
            }
        }

        db.GoalProgressEntries.Add(new GoalProgressEntry
        {
            GoalId = goalId,
            RecordedUtc = clock.UtcNow,
            Percent = percent,
            Value = value,
            Target = target,
            Source = source,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
        });
    }

    /// <summary>After a task changed: records progress for the affected goals that are driven by linked tasks. Saves.</summary>
    public async Task RecordTaskChangeAsync(IAppDbContext db, IEnumerable<Guid?> goalIds, CancellationToken ct)
    {
        var ids = goalIds.Where(g => g is not null).Select(g => g!.Value).Distinct().ToList();
        if (ids.Count == 0)
        {
            return;
        }

        var linked = await db.Goals
            .Where(g => ids.Contains(g.Id) && g.Mode == GoalProgressMode.LinkedTasks)
            .Select(g => g.Id)
            .ToListAsync(ct);
        foreach (var id in linked)
        {
            await RecordAsync(db, id, GoalProgressSource.TaskChanged, ct: ct);
        }

        if (linked.Count > 0)
        {
            await db.SaveChangesAsync(ct);
        }
    }
}
