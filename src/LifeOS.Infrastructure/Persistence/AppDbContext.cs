using LifeOS.Application.Abstractions;
using LifeOS.Domain.Common;
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
