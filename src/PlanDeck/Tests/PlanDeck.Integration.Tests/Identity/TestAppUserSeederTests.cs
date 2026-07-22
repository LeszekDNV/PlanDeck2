using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using PlanDeck.Application.Domain;
using PlanDeck.Infrastructure.Persistence;
using PlanDeck.Server.Testing;

namespace PlanDeck.Identity.IntegrationTests;

[TestFixture]
public sealed class TestAppUserSeederTests
{
    [Test]
    public async Task SeedAsync_Repeatedly_CreatesExactlyTheDeterministicUsers()
    {
        var options = new DbContextOptionsBuilder<PlanDeckDbContext>()
            .UseInMemoryDatabase($"TestAppUserSeeder-{Guid.NewGuid():N}")
            .Options;
        var seeder = new TestAppUserSeeder(options);

        await seeder.SeedAsync();
        await seeder.SeedAsync();

        await using var db = new PlanDeckDbContext(
            options,
            new TestCurrentUserContext(TestMemberIdentities.TenantId));
        var users = await db.AppUsers.OrderBy(user => user.Email).ToListAsync();

        Assert.That(users, Has.Count.EqualTo(TestMemberIdentities.All.Count));
        Assert.Multiple(() =>
        {
            foreach (var identity in TestMemberIdentities.All)
            {
                Assert.That(users, Has.One.Matches<AppUser>(user =>
                    user.Id == identity.AppUserId
                    && user.EntraObjectId == identity.EntraObjectId
                    && user.Email == identity.Email
                    && user.IsActive));
            }
        });
    }

    [TestCase("Development", true, true)]
    [TestCase("Testing", true, true)]
    [TestCase("Production", true, false)]
    [TestCase("Development", false, false)]
    public void ShouldRun_RequiresTestAuthAndNonProductionEnvironment(
        string environmentName,
        bool useTestScheme,
        bool expected)
    {
        var configuration = new ConfigurationManager();
        configuration["Authentication:UseTestScheme"] = useTestScheme.ToString();
        var environment = new TestEnvironment { EnvironmentName = environmentName };

        Assert.That(TestAppUserSeeder.ShouldRun(environment, configuration), Is.EqualTo(expected));
    }

    private sealed class TestCurrentUserContext(Guid tenantId)
        : PlanDeck.Application.Abstractions.ICurrentUserContext
    {
        public Guid TenantId => tenantId;
        public Guid UserId => Guid.Empty;
        public bool IsAuthenticated => false;
        public string? DisplayName => null;
        public string? Email => null;
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = nameof(PlanDeck);
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
