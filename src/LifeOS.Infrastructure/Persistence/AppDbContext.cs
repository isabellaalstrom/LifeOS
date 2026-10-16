using LifeOS.Application.Abstractions;
using LifeOS.Domain.Common;
using LifeOS.Domain.Goals;
using LifeOS.Domain.Habits;
using LifeOS.Domain.LifeAreas;
using LifeOS.Domain.Planning;
using LifeOS.Domain.Projects;
using LifeOS.Domain.Tasks;
using LifeOS.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace LifeOS.Infrastructure.Persistence;

public class AppDbContext : IdentityDbContext<AppUser>, IAppDbContext
{
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _timeProvider;

    public AppDbContext(DbContextOptions<AppDbContext> options, ICurrentUser currentUser, TimeProvider timeProvider)
        : base(options)
    {
        _currentUser = currentUser;
        _timeProvider = timeProvider;
    }

    public DbSet<LifeArea> LifeAreas => Set<LifeArea>();

    public DbSet<Project> Projects => Set<Project>();

    public DbSet<TaskItem> Tasks => Set<TaskItem>();

    public DbSet<Sprint> Sprints => Set<Sprint>();

    public DbSet<SprintTask> SprintTasks => Set<SprintTask>();

    public DbSet<SprintScopeChange> ScopeChanges => Set<SprintScopeChange>();

    public DbSet<Goal> Goals => Set<Goal>();

    public DbSet<GoalMilestone> GoalMilestones => Set<GoalMilestone>();

    public DbSet<GoalProgressEntry> GoalProgressEntries => Set<GoalProgressEntry>();

    public DbSet<Habit> Habits => Set<Habit>();

    public DbSet<HabitCompletion> HabitCompletions => Set<HabitCompletion>();

    public async Task<IAppTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default) =>
        new EfTransaction(await Database.BeginTransactionAsync(cancellationToken));

    private sealed class EfTransaction(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction inner) : IAppTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken = default) => inner.CommitAsync(cancellationToken);

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }

    // Evaluated per context instance by EF; null (anonymous) matches no rows because OwnerId is never null.
    private string? CurrentOwnerId => _currentUser.UserId;

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // SQLite stores DateTimeOffset as text by default, which cannot be ordered or compared reliably.
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<DateTimeOffsetToBinaryConverter>();

        // Enums are stored as readable text so the database stays understandable and reorder-safe.
        configurationBuilder.Properties<ProjectStatus>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<TaskTriage>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<TaskItemStatus>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<TaskPriority>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<ScopeChangeKind>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<ScopeChangeReason>().HaveConversion<string>().HaveMaxLength(30);
        configurationBuilder.Properties<GoalHorizon>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<GoalStatus>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<GoalProgressMode>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<GoalProgressSource>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<HabitPeriod>().HaveConversion<string>().HaveMaxLength(20);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<LifeArea>(b =>
        {
            b.Property(x => x.Name).HasMaxLength(100).IsRequired();
            b.Property(x => x.Color).HasMaxLength(20);
            b.HasIndex(x => new { x.OwnerId, x.SortOrder });
        });

        modelBuilder.Entity<Project>(b =>
        {
            b.Property(x => x.Name).HasMaxLength(150).IsRequired();
            b.Property(x => x.Description).HasMaxLength(2000);
            b.HasOne(x => x.LifeArea).WithMany().HasForeignKey(x => x.LifeAreaId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => new { x.OwnerId, x.Status });
        });

        modelBuilder.Entity<TaskItem>(b =>
        {
            b.Property(x => x.Title).HasMaxLength(300).IsRequired();
            b.Property(x => x.Notes).HasMaxLength(4000);
            b.HasOne(x => x.LifeArea).WithMany().HasForeignKey(x => x.LifeAreaId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.Parent).WithMany(x => x.Subtasks).HasForeignKey(x => x.ParentTaskId).OnDelete(DeleteBehavior.Restrict);
            b.Ignore(x => x.IsOpen);
            b.HasIndex(x => new { x.OwnerId, x.Triage, x.Status });
            b.HasIndex(x => new { x.OwnerId, x.ProjectId });
            b.HasIndex(x => new { x.OwnerId, x.DueDate });
            b.HasIndex(x => new { x.OwnerId, x.FocusDate });
            b.HasIndex(x => new { x.OwnerId, x.ScheduledDate });
            b.HasIndex(x => x.ParentTaskId);
        });

        modelBuilder.Entity<Sprint>(b =>
        {
            b.Property(x => x.Goal).HasMaxLength(500);
            b.Ignore(x => x.IsClosed);
            b.HasIndex(x => new { x.OwnerId, x.WeekStart }).IsUnique();
        });

        modelBuilder.Entity<SprintTask>(b =>
        {
            b.HasOne(x => x.Sprint).WithMany(x => x.Tasks).HasForeignKey(x => x.SprintId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.Task).WithMany().HasForeignKey(x => x.TaskItemId).OnDelete(DeleteBehavior.Restrict);
            b.Ignore(x => x.IsActive);
            b.HasIndex(x => new { x.SprintId, x.TaskItemId }).IsUnique();
            b.HasIndex(x => x.TaskItemId);
        });

        modelBuilder.Entity<Goal>(b =>
        {
            b.Property(x => x.Title).HasMaxLength(200).IsRequired();
            b.Property(x => x.Description).HasMaxLength(4000);
            b.Property(x => x.Unit).HasMaxLength(30);
            b.HasOne(x => x.LifeArea).WithMany().HasForeignKey(x => x.LifeAreaId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.Parent).WithMany(x => x.Children).HasForeignKey(x => x.ParentGoalId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => new { x.OwnerId, x.Horizon, x.PeriodStart });
            b.HasIndex(x => x.ParentGoalId);
        });

        modelBuilder.Entity<GoalMilestone>(b =>
        {
            b.Property(x => x.Title).HasMaxLength(200).IsRequired();
            b.Ignore(x => x.IsDone);
            b.HasOne(x => x.Goal).WithMany(x => x.Milestones).HasForeignKey(x => x.GoalId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => new { x.GoalId, x.SortOrder });
        });

        // History is plain rows keyed by goal; no navigation so it can be written without loading the goal.
        modelBuilder.Entity<GoalProgressEntry>(b =>
        {
            b.Property(x => x.Note).HasMaxLength(500);
            b.HasIndex(x => new { x.GoalId, x.RecordedUtc });
        });

        modelBuilder.Entity<Habit>(b =>
        {
            b.Property(x => x.Name).HasMaxLength(150).IsRequired();
            b.Property(x => x.Notes).HasMaxLength(2000);
            b.HasOne(x => x.LifeArea).WithMany().HasForeignKey(x => x.LifeAreaId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.Goal).WithMany().HasForeignKey(x => x.GoalId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => new { x.OwnerId, x.IsArchived });
        });

        modelBuilder.Entity<HabitCompletion>(b =>
        {
            b.Property(x => x.Note).HasMaxLength(500);
            b.HasOne(x => x.Habit).WithMany().HasForeignKey(x => x.HabitId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => new { x.HabitId, x.Date });
            // One completion per generated task, ever: finishing/reopening a task can never be counted twice.
            b.HasIndex(x => x.TaskItemId).IsUnique();
        });

        modelBuilder.Entity<TaskItem>(b =>
        {
            b.HasOne(x => x.Goal).WithMany().HasForeignKey(x => x.GoalId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.Habit).WithMany().HasForeignKey(x => x.HabitId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => new { x.OwnerId, x.GoalId });
            // At most one generated task per habit, week and occurrence number (NULLs are distinct, so normal tasks are unaffected).
            b.HasIndex(x => new { x.HabitId, x.GeneratedForPeriodStart, x.GeneratedIndex }).IsUnique();
        });

        modelBuilder.Entity<Project>(b =>
        {
            b.HasOne(x => x.Goal).WithMany().HasForeignKey(x => x.GoalId).OnDelete(DeleteBehavior.Restrict);
        });

        // Deliberately no foreign keys: the audit trail stands on its own and is never touched by other deletes.
        modelBuilder.Entity<SprintScopeChange>(b =>
        {
            b.Property(x => x.TaskTitle).HasMaxLength(300).IsRequired();
            b.HasIndex(x => new { x.SprintId, x.OccurredUtc });
            b.HasIndex(x => x.TaskItemId);
        });

        foreach (var entityType in modelBuilder.Model.GetEntityTypes()
                     .Where(t => typeof(OwnedEntity).IsAssignableFrom(t.ClrType)))
        {
            modelBuilder.Entity(entityType.ClrType, b =>
            {
                b.Property(nameof(OwnedEntity.OwnerId)).IsRequired().HasMaxLength(450);
                b.HasIndex(nameof(OwnedEntity.OwnerId));
            });
            SetOwnerFilter(modelBuilder, entityType.ClrType);
        }
    }

    private void SetOwnerFilter(ModelBuilder modelBuilder, Type clrType)
    {
        var method = typeof(AppDbContext)
            .GetMethod(nameof(ApplyOwnerFilter), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .MakeGenericMethod(clrType);
        method.Invoke(this, [modelBuilder]);
    }

    private void ApplyOwnerFilter<T>(ModelBuilder modelBuilder) where T : OwnedEntity =>
        modelBuilder.Entity<T>().HasQueryFilter(e => e.OwnerId == CurrentOwnerId);

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampOwnedEntities();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        StampOwnedEntities();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void StampOwnedEntities()
    {
        var now = _timeProvider.GetUtcNow();
        foreach (var entry in ChangeTracker.Entries<OwnedEntity>())
        {
            if (entry.Entity is SprintScopeChange && entry.State is EntityState.Modified or EntityState.Deleted)
            {
                throw new InvalidOperationException("Sprint scope changes are an append-only audit and cannot be changed or deleted.");
            }

            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.OwnerId = _currentUser.UserId
                        ?? throw new InvalidOperationException("Cannot save owned data without an authenticated user.");
                    entry.Entity.CreatedUtc = now;
                    entry.Entity.UpdatedUtc = now;
                    break;
                case EntityState.Modified:
                    if (entry.Property(x => x.OwnerId).IsModified)
                    {
                        throw new InvalidOperationException("Changing the owner of an entity is not allowed.");
                    }

                    entry.Entity.UpdatedUtc = now;
                    break;
            }
        }
    }
}
