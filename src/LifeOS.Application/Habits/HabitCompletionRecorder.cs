using LifeOS.Application.Abstractions;
using LifeOS.Application.Goals;
using LifeOS.Domain.Goals;
using LifeOS.Domain.Habits;
using Microsoft.EntityFrameworkCore;

namespace LifeOS.Application.Habits;

/// <summary>
/// Adds and removes habit completions together with their effect on a linked numeric goal and its history.
/// Runs inside the caller's transaction; saves as it goes.
/// A completion made by finishing a generated task carries the task id, which is unique in the database: it can never exist twice.
/// </summary>
public sealed class HabitCompletionRecorder(IUserClock clock, GoalProgressRecorder goals)
{
    public async Task AddAsync(IAppDbContext db, Habit habit, DateOnly date, Guid? taskId, string? note, CancellationToken ct)
    {
        db.HabitCompletions.Add(new HabitCompletion
        {
            HabitId = habit.Id,
            Date = date,
            CompletedUtc = clock.UtcNow,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            TaskItemId = taskId,
        });

        await ApplyGoalContributionAsync(db, habit, +habit.GoalIncrement, ct);
    }

    public async Task RemoveAsync(IAppDbContext db, Habit habit, HabitCompletion completion, CancellationToken ct)
    {
        db.HabitCompletions.Remove(completion);
        await ApplyGoalContributionAsync(db, habit, -habit.GoalIncrement, ct);
    }

    private async Task ApplyGoalContributionAsync(IAppDbContext db, Habit habit, decimal delta, CancellationToken ct)
    {
        if (habit.GoalId is not { } goalId)
        {
            await db.SaveChangesAsync(ct);
            return;
        }

        // Only numeric goals take a contribution. Task-driven goals already count the generated tasks themselves.
        var goal = await db.Goals.FirstOrDefaultAsync(g => g.Id == goalId, ct);
        if (goal is { Mode: GoalProgressMode.Numeric })
        {
            goal.CurrentValue = (goal.CurrentValue ?? goal.StartValue ?? 0m) + delta;
        }

        await db.SaveChangesAsync(ct);

        if (goal is { Mode: GoalProgressMode.Numeric })
        {
            await goals.RecordAsync(db, goalId, GoalProgressSource.Habit, habit.Name, ct: ct);
            await db.SaveChangesAsync(ct);
        }
    }
}
