using Microsoft.EntityFrameworkCore;
using PlanDeck.Application.Abstractions;
using PlanDeck.Application.Domain;
using PlanDeck.Infrastructure.Persistence;

namespace PlanDeck.Server.Testing;

public sealed class TestAppUserSeeder(DbContextOptions<PlanDeckDbContext> options)
{
    public static bool ShouldRun(IHostEnvironment environment, IConfiguration configuration) =>
        configuration.GetValue<bool>("Authentication:UseTestScheme")
        && (environment.IsDevelopment() || environment.IsEnvironment("Testing"));

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        await using var db = new PlanDeckDbContext(
            options,
            new SeedCurrentUserContext(TestMemberIdentities.TenantId));

        foreach (var identity in TestMemberIdentities.All)
        {
            var normalizedEmail = identity.Email.ToUpperInvariant();
            var user = await db.AppUsers.SingleOrDefaultAsync(
                candidate => candidate.Id == identity.AppUserId
                    || candidate.EntraObjectId == identity.EntraObjectId
                    || candidate.NormalizedEmail == normalizedEmail,
                cancellationToken);

            if (user is null)
            {
                db.AppUsers.Add(new AppUser
                {
                    Id = identity.AppUserId,
                    TenantId = TestMemberIdentities.TenantId,
                    EntraObjectId = identity.EntraObjectId,
                    DisplayName = identity.DisplayName,
                    Email = identity.Email,
                    IsActive = true
                });
                continue;
            }

            if (user.Id != identity.AppUserId
                || user.EntraObjectId != identity.EntraObjectId
                || !string.Equals(user.NormalizedEmail, normalizedEmail, StringComparison.Ordinal))
            {
                if (user.Id != identity.AppUserId)
                {
                    // Recover from stale local test data: re-create deterministic identity id and
                    // clear memberships tied to the legacy id that would violate FK/unique constraints.
                    await db.ProjectMembers
                        .Where(member => member.TenantId == TestMemberIdentities.TenantId
                            && member.AppUserId == user.Id)
                        .ExecuteDeleteAsync(cancellationToken);
                    await db.TeamMembers
                        .Where(member => member.TenantId == TestMemberIdentities.TenantId
                            && member.AppUserId == user.Id)
                        .ExecuteDeleteAsync(cancellationToken);

                    db.AppUsers.Remove(user);
                    db.AppUsers.Add(new AppUser
                    {
                        Id = identity.AppUserId,
                        TenantId = TestMemberIdentities.TenantId,
                        EntraObjectId = identity.EntraObjectId,
                        DisplayName = identity.DisplayName,
                        Email = identity.Email,
                        IsActive = true
                    });

                    continue;
                }
            }

            user.DisplayName = identity.DisplayName;
            user.Email = identity.Email;
            user.IsActive = true;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private sealed class SeedCurrentUserContext(Guid tenantId) : ICurrentUserContext
    {
        public Guid TenantId => tenantId;

        public Guid UserId => Guid.Empty;

        public bool IsAuthenticated => false;

        public string? DisplayName => null;

        public string? Email => null;
    }
}

