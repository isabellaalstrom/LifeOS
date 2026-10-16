using LifeOS.Domain.Common;
using LifeOS.Domain.LifeAreas;

namespace LifeOS.Domain.Projects;

public enum ProjectStatus
{
    Active,
    OnHold,
    Completed,
    Archived,
}

public class Project : OwnedEntity
{
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public Guid? LifeAreaId { get; set; }

    public LifeArea? LifeArea { get; set; }

    public ProjectStatus Status { get; set; } = ProjectStatus.Active;
}
