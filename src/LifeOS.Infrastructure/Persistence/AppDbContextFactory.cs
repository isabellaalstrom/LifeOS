using LifeOS.Application.Abstractions;
using LifeOS.Domain.Time;
using Microsoft.EntityFrameworkCore;

namespace LifeOS.Infrastructure.Persistence;

/// <summary>Creates a short-lived, owner-scoped context per operation (the factory is scoped, so it sees the current user).</summary>
public sealed class AppDbContextFactory(IDbContextFactory<AppDbContext> factory) : IAppDbContextFactory
{
    public IAppDbContext Create() => factory.CreateDbContext();
}

/// <summary>Resolves "now" and "today" in the signed-in user's time zone (AppUser.TimeZoneId, default Europe/Stockholm).</summary>
public sealed class UserClock(IDbContextFactory<AppDbContext> factory, ICurrentUser user, TimeProvider time) : IUserClock
{
    public const string DefaultZoneId = "Europe/Stockholm";

    private TimeZoneInfo? _zone;

    public TimeZoneInfo Zone => _zone ?? Resolve();

    public DateTimeOffset UtcNow => time.GetUtcNow();

    public DateOnly Today => PeriodCalculator.LocalDate(UtcNow, Zone);

    private TimeZoneInfo Resolve()
    {
        string? id = null;
        var userId = user.UserId;
        if (userId is not null)
        {
            using var db = factory.CreateDbContext();
            id = db.Users.Where(u => u.Id == userId).Select(u => u.TimeZoneId).FirstOrDefault();
        }

        TimeZoneInfo zone;
        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(id ?? DefaultZoneId);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            zone = TimeZoneInfo.Utc;
        }

        // Only cache once we know whose zone it is.
        if (userId is not null)
        {
            _zone = zone;
        }

        return zone;
    }
}
