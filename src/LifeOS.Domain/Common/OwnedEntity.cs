namespace LifeOS.Domain.Common;

/// <summary>Base for all user-owned data. OwnerId is set on insert and enforced by a global query filter.</summary>
public abstract class OwnedEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Id of the owning user. Assigned by the persistence layer from the current user.</summary>
    public string OwnerId { get; set; } = string.Empty;

    public DateTimeOffset CreatedUtc { get; set; }

    public DateTimeOffset UpdatedUtc { get; set; }
}
