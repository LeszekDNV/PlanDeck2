using Microsoft.EntityFrameworkCore;
using PlanDeck.Application.Abstractions;
using PlanDeck.Application.Domain;
using PlanDeck.Infrastructure.Identity;
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
            var appUser = await db.AppUsers.SingleOrDefaultAsync(
                candidate => candidate.Id == identity.AppUserId,
                cancellationToken);

            var (firstName, lastName) = SplitName(identity.DisplayName);

            if (appUser is null)
            {
                EnsureApplicationUser(db, identity, normalizedEmail);
                db.AppUsers.Add(new AppUser
                {
                    Id = identity.AppUserId,
                    TenantId = TestMemberIdentities.TenantId,
                    FirstName = firstName,
                    LastName = lastName,
                    Role = identity.Role,
                    IsActive = true
                });
                continue;
            }

            appUser.FirstName = firstName;
            appUser.LastName = lastName;
            appUser.IsActive = true;
            appUser.Role = identity.Role;

            EnsureApplicationUser(db, identity, normalizedEmail);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static void EnsureApplicationUser(
        PlanDeckDbContext db,
        TestMemberIdentity identity,
        string normalizedEmail)
    {
        var applicationUser = db.Users.Local.SingleOrDefault(u => u.Id == identity.AppUserId)
            ?? db.Users.AsNoTracking().SingleOrDefault(u => u.Id == identity.AppUserId);

        if (applicationUser is not null)
        {
            return;
        }

        db.Users.Add(new ApplicationUser
        {
            Id = identity.AppUserId,
            UserName = identity.UserName,
            NormalizedUserName = identity.UserName.ToUpperInvariant(),
            Email = identity.Email,
            NormalizedEmail = normalizedEmail,
            EmailConfirmed = true,
            SecurityStamp = Guid.NewGuid().ToString(),
            LockoutEnabled = false
        });
    }

    private static (string FirstName, string LastName) SplitName(string displayName)
    {
        var parts = displayName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 1
            ? (parts[0], string.Join(' ', parts[1..]))
            : (displayName, string.Empty);
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

