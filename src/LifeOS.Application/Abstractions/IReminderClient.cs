namespace LifeOS.Application.Abstractions;

/// <summary>One item of the connected reminders list. <see cref="Due"/> is a plain date; <see cref="DueAt"/> a moment in time (UTC). At most one is set.</summary>
public sealed record ReminderItem(string Uid, string Summary, bool Done, DateOnly? Due, DateTimeOffset? DueAt);

public sealed class ReminderException(string message) : Exception(message);

/// <summary>The list of reminders on the owner's phone, reached through Home Assistant. Optional: without it nothing changes.</summary>
public interface IReminderClient
{
    bool IsConfigured { get; }

    /// <summary>The connected list's name for display (for example the entity id). Null when not configured.</summary>
    string? ListName { get; }

    /// <summary>All items, open and completed. Throws <see cref="ReminderException"/> when the list cannot be read.</summary>
    Task<IReadOnlyList<ReminderItem>> GetItemsAsync(CancellationToken cancellationToken = default);

    Task AddAsync(string summary, DateOnly? due, CancellationToken cancellationToken = default);

    /// <summary>Changes the given fields of an item. Null means "leave as is".</summary>
    Task UpdateAsync(string uid, string? summary, DateOnly? due, bool? done, CancellationToken cancellationToken = default);
}
