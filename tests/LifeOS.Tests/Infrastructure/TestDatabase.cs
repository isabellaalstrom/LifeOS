using LifeOS.Application.Abstractions;
using LifeOS.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LifeOS.Tests.Infrastructure;

public sealed class FakeCurrentUser(string? userId) : ICurrentUser
{
    public string? UserId { get; set; } = userId;
}

/// <summary>A migrated SQLite database in a temp directory, removed on dispose.</summary>
public sealed class TestDatabase : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "lifeos-tests-" + Guid.NewGuid().ToString("N"));

    public TestDatabase()
    {
        Directory.CreateDirectory(_directory);
        Options = new LifeOSOptions { DataPath = _directory };
        using var context = CreateContext(new FakeCurrentUser(null));
        context.Database.Migrate();
    }

    public LifeOSOptions Options { get; }

    public AppDbContext CreateContext(ICurrentUser user) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(Options.ConnectionString).Options, user, TimeProvider.System);

    public SqliteBackupService CreateBackupService() => new(Microsoft.Extensions.Options.Options.Create(Options));

    public string PathInside(string fileName) => Path.Combine(_directory, fileName);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort temp cleanup.
        }
    }
}
