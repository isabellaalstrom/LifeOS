using LifeOS.Application.Abstractions;
using LifeOS.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;

namespace LifeOS.Infrastructure.Persistence;

public sealed class UserProfileStore(IDbContextFactory<AppDbContext> factory, ICurrentUser currentUser) : IUserProfileStore
{
    public async Task<string> GetTimeZoneIdAsync(CancellationToken cancellationToken = default)
    {
        var userId = currentUser.UserId;
        if (userId is null)
        {
            return UserClock.DefaultZoneId;
        }

        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await db.Users.Where(u => u.Id == userId).Select(u => u.TimeZoneId).FirstOrDefaultAsync(cancellationToken)
               ?? UserClock.DefaultZoneId;
    }

    public async Task<bool> SetTimeZoneIdAsync(string zoneId, CancellationToken cancellationToken = default)
    {
        var userId = currentUser.UserId;
        if (userId is null || string.IsNullOrWhiteSpace(zoneId))
        {
            return false;
        }

        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(zoneId);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return false;
        }

        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
        {
            return false;
        }

        user.TimeZoneId = zoneId;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<string>> ListOwnerIdsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await db.Users.Select(u => u.Id).ToListAsync(cancellationToken);
    }
}
