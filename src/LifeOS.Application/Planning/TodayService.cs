using LifeOS.Application.Abstractions;
using LifeOS.Application.Organize;
using LifeOS.Domain.Tasks;
using Microsoft.EntityFrameworkCore;

namespace LifeOS.Application.Planning;

/// <summary>Daily focus and the overview dashboard. Read-only; "today" is the user's local date.</summary>
public sealed class TodayService(IAppDbContextFactory dbFactory, IUserClock clock, SprintService sprints, TaskService tasks)
{
    public const int UpcomingDeadlineDays = 14;

    public async Task<TodayView> GetTodayAsync(CancellationToken ct = default)
    {
        var today = clock.Today;
        var week = sprints.CurrentWeekStart;
        await using var db = dbFactory.Create();

        var focus = await db.Tasks
            .Where(t => t.Status != TaskItemStatus.Cancelled
                        && (t.FocusDate == today || t.ScheduledDate == today))
            .OrderBy(t => t.Status == TaskItemStatus.Done).ThenBy(t => t.ScheduledTime == null).ThenBy(t => t.ScheduledTime).ThenBy(t => t.CreatedUtc)
            .ToView(db.SprintTasks)
            .ToListAsync(ct);
        var focusIds = focus.Select(f => f.Id).ToList();

        var due = await db.Tasks
            .Where(t => (t.Status == TaskItemStatus.ToDo || t.Status == TaskItemStatus.InProgress)
                        && t.DueDate != null && t.DueDate <= today && !focusIds.Contains(t.Id))
            .OrderBy(t => t.DueDate).ThenByDescending(t => t.Priority)
            .ToView(db.SprintTasks)
            .ToListAsync(ct);

        var dueIds = due.Select(d => d.Id).ToList();
        var suggestions = await db.SprintTasks
            .Where(st => st.Sprint.WeekStart == week && st.Sprint.ClosedUtc == null && st.LeftUtc == null
                         && (st.Task.Status == TaskItemStatus.ToDo || st.Task.Status == TaskItemStatus.InProgress)
                         && !focusIds.Contains(st.TaskItemId) && !dueIds.Contains(st.TaskItemId))
            .OrderBy(st => st.Position)
            .Select(st => st.TaskItemId)
            .Take(10)
            .ToListAsync(ct);
        var suggestionViews = await db.Tasks.Where(t => suggestions.Contains(t.Id)).ToView(db.SprintTasks).ToListAsync(ct);
        var ordered = suggestions.Select(id => suggestionViews.First(v => v.Id == id)).ToList();

        return new TodayView(today, focus, due, ordered);
    }

    public async Task<OverviewView> GetOverviewAsync(CancellationToken ct = default)
    {
        var today = clock.Today;
        var board = await sprints.GetBoardAsync(today, ct);
        var activeTasks = board.ActiveTasks.Where(t => t.Task.Status != TaskItemStatus.Cancelled).ToList();
        var summary = new SprintSummary(
            board.WeekStart,
            board.IsoWeek,
            board.SprintId is not null,
            board.Goal,
            activeTasks.Count(t => t.Task.Status == TaskItemStatus.ToDo),
            activeTasks.Count(t => t.Task.Status == TaskItemStatus.InProgress),
            activeTasks.Count(t => t.Task.Status == TaskItemStatus.Done),
            board.PlannedMinutes,
            board.CapacityMinutes,
            board.Capacity,
            board.Stats);

        var todayView = await GetTodayAsync(ct);
        var horizon = today.AddDays(UpcomingDeadlineDays);

        await using var db = dbFactory.Create();
        var deadlines = await db.Tasks
            .Where(t => (t.Status == TaskItemStatus.ToDo || t.Status == TaskItemStatus.InProgress)
                        && t.DueDate != null && t.DueDate <= horizon)
            .OrderBy(t => t.DueDate).ThenByDescending(t => t.Priority)
            .Take(8)
            .ToView(db.SprintTasks)
            .ToListAsync(ct);

        return new OverviewView(
            today,
            todayView.Focus,
            summary,
            deadlines,
            await tasks.InboxCountAsync(ct),
            await sprints.ListPastSprintsToCloseAsync(ct));
    }
}
