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

        services.AddDbContext<AppDbContext>((sp, builder) =>
            builder.UseSqlite(sp.GetRequiredService<IOptions<LifeOSOptions>>().Value.ConnectionString));

        services.AddScoped<IDatabaseBackupService, SqliteBackupService>();
        services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database");
        return services;
    }
}
