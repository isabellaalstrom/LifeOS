using LifeOS.Domain.Common;

namespace LifeOS.Domain.LifeAreas;

public class LifeArea : OwnedEntity
{
    public string Name { get; set; } = string.Empty;

    public int SortOrder { get; set; }
}
