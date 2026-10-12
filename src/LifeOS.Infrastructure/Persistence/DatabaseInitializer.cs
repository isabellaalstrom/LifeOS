using LifeOS.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LifeOS.Infrastructure.Persistence;

public static class DatabaseInitializer
{
    /// <summary>
    /// Applies pending migrations at startup. Assumes a single app instance per database file.
    /// Takes a safety backup first when an existing database is about to be migrated.
    /// </summary>
    public static async Task InitializeDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var options = sp.GetRequiredService<IOptions<LifeOSOptions>>().Value;
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("LifeOS.Database");
        var db = sp.GetRequiredService<AppDbContext>();

        Directory.CreateDirectory(options.DataPath);

        var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
        if (pending.Count == 0)
        {
            return;
        }

        var applied = (await db.Database.GetAppliedMigrationsAsync(cancellationToken)).Any();
        if (applied)
        {
            var backupPath = Path.Combine(options.DataPath, "backups", $"pre-migration-{DateTime.UtcNow:yyyyMMddHHmmss}.db");
            await sp.GetRequiredService<IDatabaseBackupService>().BackupAsync(backupPath, cancellationToken);
            logger.LogInformation("Created pre-migration backup at {Path}", backupPath);
        }

        logger.LogInformation("Applying {Count} pending migration(s)", pending.Count);
        await db.Database.MigrateAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", cancellationToken);
    }
}
