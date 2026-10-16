using LifeOS.Application.Abstractions;
using LifeOS.Application.Ai;
using LifeOS.Infrastructure.Ai;
using LifeOS.Infrastructure.Notifications;
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
        services.Configure<AiOptions>(configuration.GetSection(AiOptions.SectionName));
        services.Configure<OpenAiOptions>(configuration.GetSection(OpenAiOptions.SectionName));
        services.Configure<WebPushOptions>(configuration.GetSection(WebPushOptions.SectionName));
        services.Configure<NtfyOptions>(configuration.GetSection(NtfyOptions.SectionName));
        services.Configure<NotificationWorkerOptions>(configuration.GetSection(NotificationWorkerOptions.SectionName));

        // Scoped factory: each operation gets its own short-lived context that still sees the current user.
        // The plain scoped AppDbContext (Identity stores, health check) is created from the same factory.
        services.AddDbContextFactory<AppDbContext>(
            (sp, builder) => builder.UseSqlite(sp.GetRequiredService<IOptions<LifeOSOptions>>().Value.ConnectionString),
            ServiceLifetime.Scoped);
        services.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext());
        services.AddScoped<IAppDbContextFactory, AppDbContextFactory>();
        services.AddScoped<IUserClock, UserClock>();
        services.AddScoped<IUserProfileStore, UserProfileStore>();

        services.AddScoped<IDatabaseBackupService, SqliteBackupService>();
        services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database");

        // AI provider: optional. Without a key the app works the same and AI features report "not configured".
        services.AddHttpClient<IAiClient, OpenAiClient>((sp, client) =>
        {
            var o = sp.GetRequiredService<IOptions<OpenAiOptions>>().Value;
            client.BaseAddress = new Uri(o.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(Math.Max(5, o.TimeoutSeconds));
        });

        // Notifications: Web Push first, ntfy optional. Senders run inside an owner's scope.
        services.AddSingleton<VapidKeyStore>();
        services.AddScoped<WebPushSender>();
        services.AddScoped<INotificationSender>(sp => sp.GetRequiredService<WebPushSender>());
        services.AddHttpClient<NtfySender>(client => client.Timeout = TimeSpan.FromSeconds(15));
        services.AddScoped<INotificationSender>(sp => sp.GetRequiredService<NtfySender>());
        services.AddSingleton<IOwnerScopeRunner, OwnerScopeRunner>();
        services.AddHostedService<NotificationWorker>();
        return services;
    }
}
