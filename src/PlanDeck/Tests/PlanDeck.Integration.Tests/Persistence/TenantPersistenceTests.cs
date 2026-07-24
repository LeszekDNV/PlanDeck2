using Microsoft.EntityFrameworkCore;
using PlanDeck.Application.Abstractions;
using PlanDeck.Application.Domain;
using PlanDeck.Infrastructure.Identity;
using PlanDeck.Infrastructure.Persistence;

namespace PlanDeck.Integration.Tests.Persistence;

[TestFixture]
public sealed class TenantPersistenceTests
{
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Test]
    public async Task Migration_Applies_AndAppUsersTableExists()
    {
        await using var context = CreateContext(new FakeCurrentUserContext(TenantA, authenticated: true));

        await context.Database.MigrateAsync();

        var applied = await context.Database.GetAppliedMigrationsAsync();
        Assert.That(applied, Is.Not.Empty);

        // Querying the table proves it exists in the real database.
        var count = await context.AppUsers.CountAsync();
        Assert.That(count, Is.GreaterThanOrEqualTo(0));
    }

    [Test]
    public async Task Writes_AreScopedPerTenant_BothDirections()
    {
        var idA = Guid.NewGuid();
        var idB = Guid.NewGuid();
        var emailA = $"tenant-a-{idA:N}@example.com";
        var emailB = $"tenant-b-{idB:N}@example.com";

        await using (var tenantAContext = CreateContext(new FakeCurrentUserContext(TenantA, authenticated: true)))
        {
            tenantAContext.Users.Add(IdentityUser(idA, emailA));
            tenantAContext.AppUsers.Add(AppUser(idA, "Tenant", "A"));
            await tenantAContext.SaveChangesAsync();
        }

        await using (var tenantBContext = CreateContext(new FakeCurrentUserContext(TenantB, authenticated: true)))
        {
            tenantBContext.Users.Add(IdentityUser(idB, emailB));
            tenantBContext.AppUsers.Add(AppUser(idB, "Tenant", "B"));
            await tenantBContext.SaveChangesAsync();
        }

        await using var readA = CreateContext(new FakeCurrentUserContext(TenantA, authenticated: true));
        await using var readB = CreateContext(new FakeCurrentUserContext(TenantB, authenticated: true));

        // Positive direction: each tenant reads back its own row.
        Assert.That(await readA.AppUsers.AnyAsync(u => u.Id == idA), Is.True);
        Assert.That(await readB.AppUsers.AnyAsync(u => u.Id == idB), Is.True);

        // Negative direction: neither tenant sees the other's row.
        Assert.That(await readA.AppUsers.AnyAsync(u => u.Id == idB), Is.False);
        Assert.That(await readB.AppUsers.AnyAsync(u => u.Id == idA), Is.False);
    }

    [Test]
    public void Insert_WithNoTenantContext_IsRejectedFailClosed()
    {
        using var context = CreateContext(new FakeCurrentUserContext(Guid.Empty, authenticated: false));
        var id = Guid.NewGuid();
        context.Users.Add(IdentityUser(id, $"nobody-{id:N}@example.com"));
        context.AppUsers.Add(AppUser(id, "No", "tenant"));

        Assert.That(() => context.SaveChanges(), Throws.TypeOf<InvalidOperationException>());
    }

    [Test]
    public void Insert_Unauthenticated_WithExplicitTenant_IsRejectedFailClosed()
    {
        using var context = CreateContext(new FakeCurrentUserContext(Guid.Empty, authenticated: false));
        var id = Guid.NewGuid();
        context.Users.Add(IdentityUser(id, $"forged-{id:N}@example.com"));
        context.AppUsers.Add(new AppUser
        {
            Id = id,
            FirstName = "Forged",
            LastName = "tenant",
            TenantId = TenantA,
        });

        Assert.That(() => context.SaveChanges(), Throws.TypeOf<InvalidOperationException>());
    }

    [Test]
    public void Update_AttachedCrossTenantRow_IsRejected()
    {
        using var context = CreateContext(new FakeCurrentUserContext(TenantA, authenticated: true));
        var id = Guid.NewGuid();
        context.Users.Add(IdentityUser(id, $"cross-{id:N}@example.com"));
        context.AppUsers.Update(new AppUser
        {
            Id = id,
            FirstName = "Belongs",
            LastName = "to B",
            TenantId = TenantB,
        });

        Assert.That(() => context.SaveChanges(), Throws.TypeOf<InvalidOperationException>());
    }

    [Test]
    public async Task Reassigning_TenantId_IsRejected()
    {
        var id = Guid.NewGuid();
        var email = $"move-{id:N}@example.com";

        await using (var seed = CreateContext(new FakeCurrentUserContext(TenantA, authenticated: true)))
        {
            seed.Users.Add(IdentityUser(id, email));
            seed.AppUsers.Add(AppUser(id, "Tenant", "A"));
            await seed.SaveChangesAsync();
        }

        await using var context = CreateContext(new FakeCurrentUserContext(TenantA, authenticated: true));
        var row = await context.AppUsers.SingleAsync(u => u.Id == id);
        row.TenantId = TenantB;

        Assert.That(() => context.SaveChanges(), Throws.TypeOf<InvalidOperationException>());
    }

    private static PlanDeckDbContext CreateContext(ICurrentUserContext currentUser)
    {
        var options = new DbContextOptionsBuilder<PlanDeckDbContext>()
            .UseSqlServer(AspireAppFixture.ConnectionString, sql => sql.EnableRetryOnFailure())
            .Options;

        return new PlanDeckDbContext(options, currentUser);
    }

    private static ApplicationUser IdentityUser(Guid id, string email) => new()
    {
        Id = id,
        UserName = email,
        Email = email,
        NormalizedEmail = email.ToUpperInvariant(),
        NormalizedUserName = email.ToUpperInvariant(),
    };

    private static AppUser AppUser(Guid id, string firstName, string lastName) => new()
    {
        Id = id,
        FirstName = firstName,
        LastName = lastName,
        Role = TenantRole.Member,
        IsActive = true,
    };

    private sealed class FakeCurrentUserContext(Guid tenantId, bool authenticated) : ICurrentUserContext
    {
        public Guid TenantId { get; } = tenantId;

        public Guid UserId { get; } = Guid.Empty;

        public bool IsAuthenticated { get; } = authenticated;

        public string? DisplayName { get; }

        public string? Email { get; }
    }
}
