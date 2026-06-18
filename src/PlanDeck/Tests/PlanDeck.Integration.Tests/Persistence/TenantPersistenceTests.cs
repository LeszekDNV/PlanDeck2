using Microsoft.EntityFrameworkCore;
using PlanDeck.Application.Abstractions;
using PlanDeck.Application.Domain;
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
        Assert.That(applied, Does.Contain("20260618140615_InitialCreate"));

        // Querying the table proves it exists in the real database.
        var count = await context.AppUsers.CountAsync();
        Assert.That(count, Is.GreaterThanOrEqualTo(0));
    }

    [Test]
    public async Task Writes_AreScopedPerTenant_BothDirections()
    {
        var emailA = $"a-{Guid.NewGuid():N}@example.com";
        var emailB = $"b-{Guid.NewGuid():N}@example.com";

        await using (var tenantAContext = CreateContext(new FakeCurrentUserContext(TenantA, authenticated: true)))
        {
            tenantAContext.AppUsers.Add(new AppUser
            {
                Id = Guid.NewGuid(),
                DisplayName = "Tenant A user",
                Email = emailA,
            });
            await tenantAContext.SaveChangesAsync();
        }

        await using (var tenantBContext = CreateContext(new FakeCurrentUserContext(TenantB, authenticated: true)))
        {
            tenantBContext.AppUsers.Add(new AppUser
            {
                Id = Guid.NewGuid(),
                DisplayName = "Tenant B user",
                Email = emailB,
            });
            await tenantBContext.SaveChangesAsync();
        }

        await using var readA = CreateContext(new FakeCurrentUserContext(TenantA, authenticated: true));
        await using var readB = CreateContext(new FakeCurrentUserContext(TenantB, authenticated: true));

        // Positive direction: each tenant reads back its own row. If the query filter had
        // captured a one-time constant at model-build time, one of these would be invisible.
        Assert.That(await readA.AppUsers.AnyAsync(u => u.Email == emailA), Is.True);
        Assert.That(await readB.AppUsers.AnyAsync(u => u.Email == emailB), Is.True);

        // Negative direction: neither tenant sees the other's row.
        Assert.That(await readA.AppUsers.AnyAsync(u => u.Email == emailB), Is.False);
        Assert.That(await readB.AppUsers.AnyAsync(u => u.Email == emailA), Is.False);
    }

    [Test]
    public void Insert_WithNoTenantContext_IsRejectedFailClosed()
    {
        using var context = CreateContext(new FakeCurrentUserContext(Guid.Empty, authenticated: false));
        context.AppUsers.Add(new AppUser
        {
            Id = Guid.NewGuid(),
            DisplayName = "No tenant",
            Email = $"nobody-{Guid.NewGuid():N}@example.com",
        });

        Assert.That(() => context.SaveChanges(), Throws.TypeOf<InvalidOperationException>());
    }

    [Test]
    public void Insert_Unauthenticated_WithExplicitTenant_IsRejectedFailClosed()
    {
        using var context = CreateContext(new FakeCurrentUserContext(Guid.Empty, authenticated: false));
        context.AppUsers.Add(new AppUser
        {
            Id = Guid.NewGuid(),
            DisplayName = "Forged tenant",
            Email = $"forged-{Guid.NewGuid():N}@example.com",
            TenantId = TenantA,
        });

        Assert.That(() => context.SaveChanges(), Throws.TypeOf<InvalidOperationException>());
    }

    [Test]
    public void Update_AttachedCrossTenantRow_IsRejected()
    {
        using var context = CreateContext(new FakeCurrentUserContext(TenantA, authenticated: true));
        context.AppUsers.Update(new AppUser
        {
            Id = Guid.NewGuid(),
            DisplayName = "Belongs to B",
            Email = $"b-attached-{Guid.NewGuid():N}@example.com",
            TenantId = TenantB,
        });

        Assert.That(() => context.SaveChanges(), Throws.TypeOf<InvalidOperationException>());
    }

    [Test]
    public async Task Reassigning_TenantId_IsRejected()
    {
        var email = $"a-move-{Guid.NewGuid():N}@example.com";

        await using (var seed = CreateContext(new FakeCurrentUserContext(TenantA, authenticated: true)))
        {
            seed.AppUsers.Add(new AppUser
            {
                Id = Guid.NewGuid(),
                DisplayName = "Tenant A user",
                Email = email,
            });
            await seed.SaveChangesAsync();
        }

        await using var context = CreateContext(new FakeCurrentUserContext(TenantA, authenticated: true));
        var row = await context.AppUsers.SingleAsync(u => u.Email == email);
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

    private sealed class FakeCurrentUserContext(Guid tenantId, bool authenticated) : ICurrentUserContext
    {
        public Guid TenantId { get; } = tenantId;

        public Guid UserId { get; } = Guid.Empty;

        public bool IsAuthenticated { get; } = authenticated;
    }
}
