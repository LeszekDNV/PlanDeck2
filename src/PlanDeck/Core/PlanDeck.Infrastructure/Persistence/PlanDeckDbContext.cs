using System.Reflection;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using PlanDeck.Application.Abstractions;
using PlanDeck.Application.Domain;
using PlanDeck.Infrastructure.Identity;

namespace PlanDeck.Infrastructure.Persistence;

public sealed class PlanDeckDbContext(
    DbContextOptions<PlanDeckDbContext> options,
    ICurrentUserContext currentUser) : IdentityUserContext<ApplicationUser, Guid>(options)
{
    private readonly ICurrentUserContext _currentUser = currentUser;

    public DbSet<PlanDeckTenant> Tenants => Set<PlanDeckTenant>();

    public DbSet<AppUser> AppUsers => Set<AppUser>();

    public DbSet<TenantInvitation> TenantInvitations => Set<TenantInvitation>();

    public DbSet<PlanDeckProject> Projects => Set<PlanDeckProject>();

    public DbSet<ProjectMember> ProjectMembers => Set<ProjectMember>();

    public DbSet<ProjectTeam> ProjectTeams => Set<ProjectTeam>();

    public DbSet<ProjectAzureDevOpsConnection> ProjectAzureDevOpsConnections =>
        Set<ProjectAzureDevOpsConnection>();

    public DbSet<Team> Teams => Set<Team>();

    public DbSet<TeamMember> TeamMembers => Set<TeamMember>();

    public DbSet<PlanningSession> Sessions => Set<PlanningSession>();

    public DbSet<SessionTask> SessionTasks => Set<SessionTask>();

    public DbSet<SessionMember> SessionMembers => Set<SessionMember>();

    private Guid CurrentTenantId => _currentUser.TenantId;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PlanDeckDbContext).Assembly);

        modelBuilder.Entity<ApplicationUser>(builder =>
        {
            builder.Property(u => u.UserName).HasMaxLength(320);
            builder.Property(u => u.NormalizedUserName).HasMaxLength(320);
            builder.Property(u => u.Email).HasMaxLength(320);
            builder.Property(u => u.NormalizedEmail).HasMaxLength(320);

            builder.HasIndex(u => u.NormalizedUserName).IsUnique();
            builder.HasIndex(u => u.NormalizedEmail)
                .IsUnique()
                .HasFilter("[NormalizedEmail] IS NOT NULL");
        });

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
                    StampTenantOnInsert(entry.Entity);
                    if (entry.Entity is TenantEntity added)
                    {
                        if (added.Id == Guid.Empty)
                        {
                            added.Id = Guid.NewGuid();
                        }

                        added.CreatedAtUtc = now;
                        added.UpdatedAtUtc = now;
                    }

                    NormalizeMemberEmail(entry.Entity);
                    break;

                case EntityState.Modified:
                    GuardTenantOwnership(entry);
                    if (entry.Entity is TenantEntity modified)
                    {
                        modified.UpdatedAtUtc = now;
                    }

                    NormalizeMemberEmail(entry.Entity);
                    break;

                case EntityState.Deleted:
                    GuardTenantOwnership(entry);
                    break;
            }
        }
    }

    private static void NormalizeMemberEmail(ITenantScoped entity)
    {
        switch (entity)
        {
            case ProjectMember member:
                member.Email = member.Email.Trim();
                member.NormalizedEmail = member.Email.ToUpperInvariant();
                break;
            case TeamMember member:
                member.Email = member.Email.Trim();
                member.NormalizedEmail = member.Email.ToUpperInvariant();
                break;
        }
    }

    private void StampTenantOnInsert(ITenantScoped entity)
    {
        if (CurrentTenantId == Guid.Empty)
        {
            throw new InvalidOperationException(
                "Cannot persist a tenant-scoped entity without a resolvable tenant. The current user context has no tenant.");
        }

        if (entity.TenantId == Guid.Empty)
        {
            entity.TenantId = CurrentTenantId;
            return;
        }

        if (entity.TenantId != CurrentTenantId)
        {
            throw new InvalidOperationException(
                "Cannot persist a tenant-scoped entity whose TenantId differs from the current tenant.");
        }
    }

    private void GuardTenantOwnership(EntityEntry<ITenantScoped> entry)
    {
        if (CurrentTenantId == Guid.Empty)
        {
            throw new InvalidOperationException(
                "Cannot modify or delete a tenant-scoped entity without a resolvable tenant. The current user context has no tenant.");
        }

        var tenantProperty = entry.Property(e => e.TenantId);

        if (tenantProperty.OriginalValue != CurrentTenantId)
        {
            throw new InvalidOperationException(
                "Cannot modify or delete a tenant-scoped entity that belongs to a different tenant.");
        }

        if (entry.State == EntityState.Modified && tenantProperty.IsModified)
        {
            throw new InvalidOperationException(
                "TenantId is immutable; reassigning a tenant-scoped entity to a different tenant is not allowed.");
        }
    }
}
