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
                throw new InvalidOperationException(
                    "The deterministic test identity conflicts with an existing AppUser.");
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
