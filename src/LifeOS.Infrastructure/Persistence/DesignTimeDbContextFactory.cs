using LifeOS.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace LifeOS.Infrastructure.Persistence;

/// <summary>Used only by `dotnet ef` tooling.</summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=design-time.db")
            .Options;
        return new AppDbContext(options, new NoUser(), TimeProvider.System);
    }

    private sealed class NoUser : ICurrentUser
    {
        public string? UserId => null;
    }
}
