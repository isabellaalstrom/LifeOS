using LifeOS.Application.Abstractions;
using LifeOS.Domain.Common;
using LifeOS.Domain.LifeAreas;
using LifeOS.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace LifeOS.Infrastructure.Persistence;

public class AppDbContext : IdentityDbContext<AppUser>
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

    // Evaluated per context instance by EF; null (anonymous) matches no rows because OwnerId is never null.
    private string? CurrentOwnerId => _currentUser.UserId;

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // SQLite stores DateTimeOffset as text by default, which cannot be ordered or compared reliably.
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<DateTimeOffsetToBinaryConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<LifeArea>(b =>
        {
            b.Property(x => x.Name).HasMaxLength(100).IsRequired();
            b.HasIndex(x => new { x.OwnerId, x.SortOrder });
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
