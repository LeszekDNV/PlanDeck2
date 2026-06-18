using System.Reflection;
using Microsoft.EntityFrameworkCore;
using PlanDeck.Application.Abstractions;
using PlanDeck.Application.Domain;

namespace PlanDeck.Infrastructure.Persistence;

public sealed class PlanDeckDbContext(
    DbContextOptions<PlanDeckDbContext> options,
    ICurrentUserContext currentUser) : DbContext(options)
{
    private readonly ICurrentUserContext _currentUser = currentUser;

    public DbSet<AppUser> AppUsers => Set<AppUser>();

    private Guid CurrentTenantId => _currentUser.TenantId;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PlanDeckDbContext).Assembly);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(ITenantScoped).IsAssignableFrom(entityType.ClrType))
            {
                var method = typeof(PlanDeckDbContext)
                    .GetMethod(nameof(ApplyTenantFilter), BindingFlags.NonPublic | BindingFlags.Instance)!
                    .MakeGenericMethod(entityType.ClrType);
                method.Invoke(this, [modelBuilder]);
            }
        }
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampTenantAndAudit();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        StampTenantAndAudit();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void ApplyTenantFilter<T>(ModelBuilder modelBuilder)
        where T : class, ITenantScoped
    {
        modelBuilder.Entity<T>().HasQueryFilter(e => e.TenantId == CurrentTenantId);
    }

    private void StampTenantAndAudit()
    {
        var now = DateTimeOffset.UtcNow;

        foreach (var entry in ChangeTracker.Entries<ITenantScoped>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    StampTenant(entry.Entity);
                    if (entry.Entity is TenantEntity added)
                    {
                        added.CreatedAtUtc = now;
                        added.UpdatedAtUtc = now;
                    }

                    break;

                case EntityState.Modified:
                    if (entry.Entity is TenantEntity modified)
                    {
                        modified.UpdatedAtUtc = now;
                    }

                    break;
            }
        }
    }

    private void StampTenant(ITenantScoped entity)
    {
        if (entity.TenantId == Guid.Empty)
        {
            if (CurrentTenantId == Guid.Empty)
            {
                throw new InvalidOperationException(
                    "Cannot persist a tenant-scoped entity without a resolvable tenant. The current user context has no tenant.");
            }

            entity.TenantId = CurrentTenantId;
            return;
        }

        if (_currentUser.IsAuthenticated && entity.TenantId != CurrentTenantId)
        {
            throw new InvalidOperationException(
                "Cannot persist a tenant-scoped entity whose TenantId differs from the current tenant.");
        }
    }
}
