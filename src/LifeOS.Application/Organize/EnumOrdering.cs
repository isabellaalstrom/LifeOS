using System.Linq.Expressions;
using LifeOS.Domain.Goals;
using LifeOS.Domain.Projects;
using LifeOS.Domain.Tasks;

namespace LifeOS.Application.Organize;

/// <summary>
/// Enums are stored as text so the database stays readable, which means ORDER BY on the column would sort
/// alphabetically. These ranks give the intended order and translate to a CASE expression.
/// </summary>
public static class EnumOrdering
{
    /// <summary>High = 3 ... None = 0. Use with OrderByDescending.</summary>
    public static readonly Expression<Func<TaskItem, int>> PriorityRank = t =>
        t.Priority == TaskPriority.High ? 3 : t.Priority == TaskPriority.Medium ? 2 : t.Priority == TaskPriority.Low ? 1 : 0;

    /// <summary>Year (0), Quarter (1), Month (2): longest horizon first.</summary>
    public static readonly Expression<Func<Goal, int>> HorizonRank = g =>
        g.Horizon == GoalHorizon.Year ? 0 : g.Horizon == GoalHorizon.Quarter ? 1 : 2;

    /// <summary>Active, then paused, completed, archived.</summary>
    public static readonly Expression<Func<Project, int>> ProjectStatusRank = p =>
        p.Status == ProjectStatus.Active ? 0 : p.Status == ProjectStatus.OnHold ? 1 : p.Status == ProjectStatus.Completed ? 2 : 3;
}
