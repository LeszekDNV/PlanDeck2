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
    public async Task Write_UnderTenantA_IsInvisibleToTenantB()
    {
        var email = $"a-{Guid.NewGuid():N}@example.com";

        await using (var tenantAContext = CreateContext(new FakeCurrentUserContext(TenantA, authenticated: true)))
        {
            tenantAContext.AppUsers.Add(new AppUser
            {
                Id = Guid.NewGuid(),
                DisplayName = "Tenant A user",
                Email = email,
            });
            await tenantAContext.SaveChangesAsync();
        }

        await using var tenantBContext = CreateContext(new FakeCurrentUserContext(TenantB, authenticated: true));
        var visibleToB = await tenantBContext.AppUsers.AnyAsync(u => u.Email == email);

        Assert.That(visibleToB, Is.False);
    }

    [Test]
    public void Write_WithNoTenantContext_IsRejectedFailClosed()
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
