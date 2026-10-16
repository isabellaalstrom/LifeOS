using LifeOS.Domain.LifeAreas;
using LifeOS.Domain.Planning;
using LifeOS.Domain.Projects;
using LifeOS.Domain.Tasks;
using Microsoft.EntityFrameworkCore;

namespace LifeOS.Application.Abstractions;

/// <summary>
/// The data access surface the application services use. All sets are owner-filtered by the implementation.
/// Create one per operation (see <see cref="IAppDbContextFactory"/>): Blazor circuits are long-lived, so a
/// context must never be held across user interactions.
/// </summary>
public interface IAppDbContext : IAsyncDisposable
{
    DbSet<LifeArea> LifeAreas { get; }

    DbSet<Project> Projects { get; }

    DbSet<TaskItem> Tasks { get; }

    DbSet<Sprint> Sprints { get; }

    DbSet<SprintTask> SprintTasks { get; }

    DbSet<SprintScopeChange> ScopeChanges { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IAppDbContextFactory
{
    IAppDbContext Create();
}
