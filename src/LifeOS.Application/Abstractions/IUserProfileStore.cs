namespace LifeOS.Application.Abstractions;

/// <summary>The few account-level facts the application needs, kept behind an interface so the Identity types stay in Infrastructure.</summary>
public interface IUserProfileStore
{
    Task<string> GetTimeZoneIdAsync(CancellationToken cancellationToken = default);

    /// <summary>Stores an IANA time zone id. Returns false if the id is not a known time zone.</summary>
    Task<bool> SetTimeZoneIdAsync(string zoneId, CancellationToken cancellationToken = default);

    /// <summary>Ids of all owners. Used by background work that runs for each owner in turn.</summary>
    Task<IReadOnlyList<string>> ListOwnerIdsAsync(CancellationToken cancellationToken = default);
}

/// <summary>Runs work as a given owner outside of a web request (background jobs), so data access stays owner-scoped.</summary>
public interface IOwnerScopeRunner
{
    Task RunAsOwnerAsync(string ownerId, Func<IServiceProvider, Task> work, CancellationToken cancellationToken = default);
}
