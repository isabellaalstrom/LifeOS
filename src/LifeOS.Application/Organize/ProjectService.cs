using LifeOS.Application.Abstractions;
using LifeOS.Application.Planning;
using LifeOS.Domain.Projects;
using LifeOS.Domain.Tasks;
using Microsoft.EntityFrameworkCore;

namespace LifeOS.Application.Organize;

public sealed class ProjectService(IAppDbContextFactory dbFactory)
{
    public async Task<IReadOnlyList<ProjectView>> ListAsync(bool includeClosed = false, CancellationToken ct = default)
    {
        await using var db = dbFactory.Create();
        return await ToView(db, db.Projects
                .Where(p => includeClosed || p.Status == ProjectStatus.Active || p.Status == ProjectStatus.OnHold)
                .OrderBy(p => p.Status).ThenBy(p => p.Name))
            .ToListAsync(ct);
    }

    public async Task<ProjectView?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = dbFactory.Create();
        return await ToView(db, db.Projects.Where(p => p.Id == id)).FirstOrDefaultAsync(ct);
    }

    public async Task<Guid> CreateAsync(ProjectInput input, CancellationToken ct = default)
    {
        var name = RequireName(input.Name);
        await using var db = dbFactory.Create();
        var project = new Project
        {
            Name = name,
            Description = Clean(input.Description),
            LifeAreaId = input.LifeAreaId,
        };
        db.Projects.Add(project);
        await db.SaveChangesAsync(ct);
        return project.Id;
    }

    public async Task UpdateAsync(Guid id, ProjectInput input, CancellationToken ct = default)
    {
        var name = RequireName(input.Name);
        await using var db = dbFactory.Create();
        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == id, ct)
                      ?? throw new PlanningException(PlanningException.Codes.NotFound);
        project.Name = name;
        project.Description = Clean(input.Description);
        project.LifeAreaId = input.LifeAreaId;
        await db.SaveChangesAsync(ct);
    }

    public async Task SetStatusAsync(Guid id, ProjectStatus status, CancellationToken ct = default)
    {
        await using var db = dbFactory.Create();
        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == id, ct)
                      ?? throw new PlanningException(PlanningException.Codes.NotFound);
        project.Status = status;
        await db.SaveChangesAsync(ct);
    }

    // Filters and ordering are applied to the entities before this projection: EF cannot translate them afterwards.
    private static IQueryable<ProjectView> ToView(IAppDbContext db, IQueryable<Project> projects) =>
        projects.Select(p => new ProjectView(
            p.Id,
            p.Name,
            p.Description,
            p.LifeAreaId,
            p.LifeArea != null ? p.LifeArea.Name : null,
            p.Status,
            db.Tasks.Count(t => t.ProjectId == p.Id && t.ParentTaskId == null
                                && (t.Status == TaskItemStatus.ToDo || t.Status == TaskItemStatus.InProgress)),
            db.Tasks.Count(t => t.ProjectId == p.Id && t.ParentTaskId == null && t.Status == TaskItemStatus.Done)));

    private static string RequireName(string? name)
    {
        name = name?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            throw new PlanningException(PlanningException.Codes.NameRequired);
        }

        return name.Length > 150 ? name[..150] : name;
    }

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
