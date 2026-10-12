using LifeOS.Domain.LifeAreas;
using LifeOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LifeOS.Tests.Infrastructure;

public class DatabaseTests
{
    [Fact]
    public void Migrations_apply_to_an_empty_database_and_leave_no_pending_model_changes()
    {
        using var db = new TestDatabase();
        using var context = db.CreateContext(new FakeCurrentUser(null));

        Assert.Empty(context.Database.GetPendingMigrations());
        Assert.NotEmpty(context.Database.GetAppliedMigrations());
        // Fails when the model was changed without adding a migration.
        Assert.False(context.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task Backup_is_a_consistent_copy_that_can_be_restored_and_read()
    {
        using var db = new TestDatabase();
        await using (var context = db.CreateContext(new FakeCurrentUser("user-a")))
        {
            context.LifeAreas.Add(new LifeArea { Name = "Personlig utveckling" });
            await context.SaveChangesAsync();
        }

        var backupPath = db.PathInside("backups/backup.db");
        await db.CreateBackupService().BackupAsync(backupPath);
        Assert.True(File.Exists(backupPath));

        // Restore = point the app at the backup file.
        var restored = new LifeOSOptions { DataPath = Path.GetDirectoryName(backupPath)! };
        File.Move(backupPath, restored.DatabaseFilePath);
        var restoredContext = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseSqlite(restored.ConnectionString).Options,
            new FakeCurrentUser("user-a"), TimeProvider.System);
        await using (restoredContext)
        {
            Assert.Equal(["Personlig utveckling"], await restoredContext.LifeAreas.Select(x => x.Name).ToListAsync());
        }
    }

    [Fact]
    public async Task Backup_refuses_to_overwrite_an_existing_file()
    {
        using var db = new TestDatabase();
        var path = db.PathInside("existing.db");
        await File.WriteAllTextAsync(path, "x");
        await Assert.ThrowsAsync<IOException>(() => db.CreateBackupService().BackupAsync(path));
    }

    [Fact]
    public async Task Startup_initialization_migrates_a_new_database_and_is_idempotent()
    {
        var directory = Path.Combine(Path.GetTempPath(), "lifeos-init-" + Guid.NewGuid().ToString("N"));
        try
        {
            var options = new LifeOSOptions { DataPath = directory };
            var services = new ServiceCollection();
            services.AddSingleton<Microsoft.Extensions.Logging.ILoggerFactory>(NullLoggerFactory.Instance);
            services.AddSingleton(Options.Create(options));
            services.AddSingleton(TimeProvider.System);
            services.AddScoped<LifeOS.Application.Abstractions.ICurrentUser>(_ => new FakeCurrentUser(null));
            services.AddScoped<LifeOS.Application.Abstractions.IDatabaseBackupService, SqliteBackupService>();
            services.AddDbContext<AppDbContext>(o => o.UseSqlite(options.ConnectionString));
            await using var provider = services.BuildServiceProvider();

            await provider.InitializeDatabaseAsync();
            await provider.InitializeDatabaseAsync();

            Assert.True(File.Exists(options.DatabaseFilePath));
            await using var scope = provider.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Empty(await context.Database.GetPendingMigrationsAsync());
            Assert.Equal("wal", (await ReadJournalModeAsync(context)).ToLowerInvariant());
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static async Task<string> ReadJournalModeAsync(AppDbContext context)
    {
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA journal_mode;";
            return (string)(await command.ExecuteScalarAsync())!;
        }
        finally
        {
            await connection.CloseAsync();
        }
    }
}
