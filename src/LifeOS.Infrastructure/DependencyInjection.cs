using LifeOS.Application.Abstractions;
using LifeOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LifeOS.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<LifeOSOptions>(configuration.GetSection(LifeOSOptions.SectionName));

        // Scoped factory: each operation gets its own short-lived context that still sees the current user.
        // The plain scoped AppDbContext (Identity stores, health check) is created from the same factory.
        services.AddDbContextFactory<AppDbContext>(
            (sp, builder) => builder.UseSqlite(sp.GetRequiredService<IOptions<LifeOSOptions>>().Value.ConnectionString),
            ServiceLifetime.Scoped);
        services.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext());
        services.AddScoped<IAppDbContextFactory, AppDbContextFactory>();
        services.AddScoped<IUserClock, UserClock>();

        services.AddScoped<IDatabaseBackupService, SqliteBackupService>();
        services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database");
        return services;
    }
}
