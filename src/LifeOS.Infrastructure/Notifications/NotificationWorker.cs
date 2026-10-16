using LifeOS.Application.Abstractions;
using LifeOS.Application.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LifeOS.Infrastructure.Notifications;

public sealed class NotificationWorkerOptions
{
    public const string SectionName = "LifeOS:Notifications:Worker";

    public bool Enabled { get; set; } = true;

    public int IntervalSeconds { get; set; } = 60;

    public int StartupDelaySeconds { get; set; } = 20;
}

/// <summary>Runs work as one owner by putting that owner's id on a fresh scope, so every query stays owner-filtered.</summary>
public sealed class OwnerScopeRunner(IServiceScopeFactory scopes) : IOwnerScopeRunner
{
    public async Task RunAsOwnerAsync(string ownerId, Func<IServiceProvider, Task> work, CancellationToken cancellationToken = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<OwnerOverride>().UserId = ownerId;
        await work(scope.ServiceProvider);
    }
}

/// <summary>
/// Plans and sends notifications in the background. All state lives in the database (planned reminders with stable keys,
/// claim locks with expiry), so a restart or a crash cannot lose or duplicate anything: the next pass simply continues.
/// Runs a single instance per database, like the rest of the app.
/// </summary>
public sealed class NotificationWorker(
    IServiceScopeFactory scopes,
    IOptions<NotificationWorkerOptions> options,
    ILogger<NotificationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            logger.LogInformation("Notification worker is disabled");
            return;
        }

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(options.Value.StartupDelaySeconds), stoppingToken);
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(10, options.Value.IntervalSeconds)));
            do
            {
                await RunOnceAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
    }

    /// <summary>One pass over all owners. A failure for one owner never stops the others.</summary>
    public async Task RunOnceAsync(CancellationToken ct)
    {
        IReadOnlyList<string> owners;
        await using (var scope = scopes.CreateAsyncScope())
        {
            owners = await scope.ServiceProvider.GetRequiredService<IUserProfileStore>().ListOwnerIdsAsync(ct);
        }

        var runner = new OwnerScopeRunner(scopes);
        foreach (var owner in owners)
        {
            try
            {
                await runner.RunAsOwnerAsync(owner, sp => sp.GetRequiredService<NotificationRunner>().RunAsync(ct), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError("Notification pass failed for an owner: {Type}", ex.GetType().Name);
            }
        }
    }
}
