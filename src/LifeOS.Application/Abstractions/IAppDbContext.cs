using LifeOS.Domain.Ai;
using LifeOS.Domain.Goals;
using LifeOS.Domain.Habits;
using LifeOS.Domain.LifeAreas;
using LifeOS.Domain.Notifications;
using LifeOS.Domain.Planning;
using LifeOS.Domain.Projects;
using LifeOS.Domain.Retros;
using LifeOS.Domain.Tasks;
using Microsoft.EntityFrameworkCore;

namespace LifeOS.Application.Abstractions;

/// <summary>
/// The data access surface the application services use. All sets are owner-filtered by the implementation.
/// Create one per operation (see <see cref="IAppDbContextFactory"/>): Blazor circuits are long-lived, so a
/// context must never be held across user interactions.
/// </summary>
public interface IAppDbContext : IAsyncDisposable
{
    DbSet<LifeArea> LifeAreas { get; }

    DbSet<Project> Projects { get; }

    DbSet<TaskItem> Tasks { get; }

    DbSet<Sprint> Sprints { get; }

    DbSet<SprintTask> SprintTasks { get; }

    DbSet<SprintScopeChange> ScopeChanges { get; }

    DbSet<Goal> Goals { get; }

    DbSet<GoalMilestone> GoalMilestones { get; }

    DbSet<GoalProgressEntry> GoalProgressEntries { get; }

    DbSet<Habit> Habits { get; }

    DbSet<HabitCompletion> HabitCompletions { get; }

    DbSet<Retrospective> Retrospectives { get; }

    DbSet<RetroResponse> RetroResponses { get; }

    DbSet<RetroAction> RetroActions { get; }

    DbSet<RetroActionFollowUp> RetroActionFollowUps { get; }

    DbSet<UserPreference> UserPreferences { get; }

    DbSet<AiProposal> AiProposals { get; }

    DbSet<AiInsight> AiInsights { get; }

    DbSet<AiCallLog> AiCallLogs { get; }

    DbSet<AiRetroMessage> AiRetroMessages { get; }

    DbSet<NotificationPreference> NotificationPreferences { get; }

    DbSet<ScheduledNotification> ScheduledNotifications { get; }

    DbSet<NotificationDelivery> NotificationDeliveries { get; }

    DbSet<PushSubscription> PushSubscriptions { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>Groups several saves into one atomic unit (e.g. a task change plus the goal progress it causes).</summary>
    Task<IAppTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);
}

public interface IAppTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken = default);
}

public interface IAppDbContextFactory
{
    IAppDbContext Create();
}
