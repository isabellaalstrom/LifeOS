using LifeOS.Domain.Planning;
using LifeOS.Domain.Tasks;

namespace LifeOS.Application.Organize;

internal static class TaskProjection
{
    /// <summary>Projects tasks to views, including subtask counts and the week of the open sprint they are planned in.</summary>
    public static IQueryable<TaskView> ToView(this IQueryable<TaskItem> tasks, IQueryable<SprintTask> sprintTasks) =>
        tasks.Select(t => new TaskView(
            t.Id,
            t.Title,
            t.Notes,
            t.Triage,
            t.Status,
            t.Priority,
            t.EstimateMinutes,
            t.DueDate,
            t.ScheduledDate,
            t.ScheduledTime,
            t.FocusDate,
            t.LifeAreaId,
            t.LifeArea != null ? t.LifeArea.Name : null,
            t.LifeArea != null ? t.LifeArea.Color : null,
            t.ProjectId,
            t.Project != null ? t.Project.Name : null,
            t.ParentTaskId,
            t.Subtasks.Count(),
            t.Subtasks.Count(s => s.Status == TaskItemStatus.Done),
            sprintTasks
                .Where(st => st.TaskItemId == t.Id && st.LeftUtc == null && st.Sprint.ClosedUtc == null)
                .Select(st => (DateOnly?)st.Sprint.WeekStart)
                .FirstOrDefault(),
            t.CompletedUtc));
}
