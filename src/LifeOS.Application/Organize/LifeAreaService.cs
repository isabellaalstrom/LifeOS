using LifeOS.Application.Abstractions;
using LifeOS.Application.Planning;
using LifeOS.Domain.LifeAreas;
using Microsoft.EntityFrameworkCore;

namespace LifeOS.Application.Organize;

public sealed record LifeAreaView(Guid Id, string Name, string? Color, bool IsArchived, int SortOrder);

public sealed class LifeAreaService(IAppDbContextFactory dbFactory)
{
    /// <summary>Starter ideas offered on an empty list. Never created automatically.</summary>
    public static readonly string[] Suggestions =
        ["Hem", "Hälsa", "Cykling", "Personlig utveckling", "Ekonomi", "Relationer", "Arbete"];

    public async Task<IReadOnlyList<LifeAreaView>> ListAsync(bool includeArchived = false, CancellationToken ct = default)
    {
        await using var db = dbFactory.Create();
        return await db.LifeAreas
            .Where(a => includeArchived || !a.IsArchived)
            .OrderBy(a => a.SortOrder).ThenBy(a => a.Name)
            .Select(a => new LifeAreaView(a.Id, a.Name, a.Color, a.IsArchived, a.SortOrder))
            .ToListAsync(ct);
    }

    public async Task<Guid> CreateAsync(string name, string? color = null, CancellationToken ct = default)
    {
        name = RequireName(name);
        await using var db = dbFactory.Create();
        var next = (await db.LifeAreas.MaxAsync(a => (int?)a.SortOrder, ct) ?? -1) + 1;
        var area = new LifeArea { Name = name, Color = NormalizeColor(color), SortOrder = next };
        db.LifeAreas.Add(area);
        await db.SaveChangesAsync(ct);
        return area.Id;
    }

    public async Task UpdateAsync(Guid id, string name, string? color, CancellationToken ct = default)
    {
        name = RequireName(name);
        await using var db = dbFactory.Create();
        var area = await db.LifeAreas.FirstOrDefaultAsync(a => a.Id == id, ct)
                   ?? throw new PlanningException(PlanningException.Codes.NotFound);
        area.Name = name;
        area.Color = NormalizeColor(color);
        await db.SaveChangesAsync(ct);
    }

    public async Task SetArchivedAsync(Guid id, bool archived, CancellationToken ct = default)
    {
        await using var db = dbFactory.Create();
        var area = await db.LifeAreas.FirstOrDefaultAsync(a => a.Id == id, ct)
                   ?? throw new PlanningException(PlanningException.Codes.NotFound);
        area.IsArchived = archived;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Creates the chosen suggestions, skipping names that already exist (case-insensitive).</summary>
    public async Task CreateManyAsync(IEnumerable<string> names, CancellationToken ct = default)
    {
        await using var db = dbFactory.Create();
        var existing = (await db.LifeAreas.Select(a => a.Name).ToListAsync(ct))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var next = (await db.LifeAreas.MaxAsync(a => (int?)a.SortOrder, ct) ?? -1) + 1;

        foreach (var name in names.Select(n => n.Trim()).Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (existing.Add(name))
            {
                db.LifeAreas.Add(new LifeArea { Name = name, SortOrder = next++ });
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private static string RequireName(string name)
    {
        name = name?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            throw new PlanningException(PlanningException.Codes.NameRequired);
        }

        return name.Length > 100 ? name[..100] : name;
    }

    private static string? NormalizeColor(string? color) => string.IsNullOrWhiteSpace(color) ? null : color.Trim();
}
