using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using PlanDeck.Infrastructure.Persistence;
using PlanDeck.Server.Identity;

// Outside PlanDeck.Integration.Tests so the Aspire fixture does not start for in-memory identity tests.
namespace PlanDeck.Identity.IntegrationTests;

[TestFixture]
public sealed class AppUserProvisionerTests
{
    private static readonly Guid TenantId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly Guid EntraObjectId =
        Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Test]
    public async Task RepeatedProvisioning_UpdatesOneStableAppUser()
    {
        var accessor = new RequestPrincipalAccessor();
        var currentUser = new HttpContextCurrentUserContext(
            new HttpContextAccessor(),
            accessor);
        var options = new DbContextOptionsBuilder<PlanDeckDbContext>()
            .UseInMemoryDatabase($"AppUserProvisioner-{Guid.NewGuid():N}")
            .Options;

        await using var db = new PlanDeckDbContext(options, currentUser);
        var provisioner = new AppUserProvisioner(
            new AppUserRepository(db),
            accessor);

        var firstId = await provisioner.ProvisionAsync(
            BuildPrincipal("First Name", "User@Example.com"),
            CancellationToken.None);
        var secondId = await provisioner.ProvisionAsync(
            BuildPrincipal("Updated Name", "user@example.com"),
            CancellationToken.None);

        var users = await db.AppUsers.IgnoreQueryFilters().ToListAsync();
        Assert.Multiple(() =>
        {
            Assert.That(secondId, Is.EqualTo(firstId));
            Assert.That(users, Has.Count.EqualTo(1));
            Assert.That(users[0].DisplayName, Is.EqualTo("Updated Name"));
            Assert.That(users[0].Email, Is.EqualTo("user@example.com"));
            Assert.That(users[0].NormalizedEmail, Is.EqualTo("USER@EXAMPLE.COM"));
            Assert.That(users[0].IsActive, Is.True);
        });
    }

    [Test]
    public void MissingExternalObjectId_IsRejected()
    {
        var accessor = new RequestPrincipalAccessor();
        var currentUser = new HttpContextCurrentUserContext(
            new HttpContextAccessor(),
            accessor);
        var options = new DbContextOptionsBuilder<PlanDeckDbContext>()
            .UseInMemoryDatabase($"AppUserProvisioner-{Guid.NewGuid():N}")
            .Options;
        using var db = new PlanDeckDbContext(options, currentUser);
        var provisioner = new AppUserProvisioner(
            new AppUserRepository(db),
            accessor);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(PlanDeckIdentity.TenantIdClaim, TenantId.ToString()),
            new Claim("email", "user@example.com")
        ], "Test"));

        Assert.That(
            async () => await provisioner.ProvisionAsync(principal, CancellationToken.None),
            Throws.TypeOf<InvalidOperationException>());
    }

    private static ClaimsPrincipal BuildPrincipal(string displayName, string email) =>
        new(new ClaimsIdentity(
        [
            new Claim(PlanDeckIdentity.TenantIdClaim, TenantId.ToString()),
            new Claim(PlanDeckIdentity.EntraObjectIdClaim, EntraObjectId.ToString()),
            new Claim("name", displayName),
            new Claim("email", email)
        ], "Test"));
}
