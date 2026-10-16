using LifeOS.Domain.Common;

namespace LifeOS.Domain.LifeAreas;

public class LifeArea : OwnedEntity
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional CSS color (e.g. #3b6ea5) used for chips.</summary>
    public string? Color { get; set; }

    public bool IsArchived { get; set; }

    public int SortOrder { get; set; }
}
