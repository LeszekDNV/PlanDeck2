using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PlanDeck.Application.Domain;
using PlanDeck.Infrastructure.Persistence;
using PlanDeck.Server;
using PlanDeck.Server.Testing;

// Deliberately NOT under PlanDeck.Integration.Tests: the AspireAppFixture [SetUpFixture] lives in
// that namespace and would boot Aspire (Podman). These endpoint tests run against an in-memory
// WebApplicationFactory and must stay out of that scope.
namespace PlanDeck.Identity.IntegrationTests;

[TestFixture]
public sealed class E2eScenarioEndpointTests
{
    private const string ScenarioToken = "integration-scenario-token";

    private WebApplicationFactory<ServerEntryPoint> _testingFactory = null!;
    private WebApplicationFactory<ServerEntryPoint> _productionFactory = null!;
    private string _databaseName = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _databaseName = $"PlanDeckE2eScenarioTests-{Guid.NewGuid():N}";
        _testingFactory = CreateFactory(
            environment: "Testing",
            useTestScheme: true,
            scenarioToken: ScenarioToken,
            _databaseName);
        _productionFactory = CreateFactory(
            environment: "Production",
            useTestScheme: false,
            scenarioToken: ScenarioToken,
            _databaseName);
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _testingFactory.Dispose();
        _productionFactory.Dispose();
    }

    [Test]
    public async Task SeedScenario_MissingToken_ReturnsUnauthorized()
    {
        var response = await CreateClient(_testingFactory).PostAsJsonAsync(
            "/testing/e2e-scenarios/",
            new E2eScenarioSeedRequest(Guid.NewGuid()));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task SeedScenario_InvalidToken_ReturnsUnauthorized()
    {
        var client = CreateClient(_testingFactory);
        client.DefaultRequestHeaders.Add(E2eScenarioEndpoints.TokenHeaderName, "wrong-token");

        var response = await client.PostAsJsonAsync(
            "/testing/e2e-scenarios/",
            new E2eScenarioSeedRequest(Guid.NewGuid()));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task SeedAndCleanupScenario_ValidToken_IsScopedByRunIdAndIdempotent()
    {
        var runId = Guid.NewGuid();
        var client = CreateAuthorizedClient();

        var seedResponse = await client.PostAsJsonAsync(
            "/testing/e2e-scenarios/",
            new E2eScenarioSeedRequest(runId, SessionStatus.Active, TaskCount: 2));
        Assert.That(seedResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var seed = await seedResponse.Content.ReadFromJsonAsync<E2eScenarioSeedResponse>();
        Assert.That(seed, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(seed!.RunId, Is.EqualTo(runId));
            Assert.That(seed.ProjectId, Is.Not.EqualTo(Guid.Empty));
            Assert.That(seed.SessionId, Is.Not.EqualTo(Guid.Empty));
        });

        var cleanupResponse = await client.DeleteAsync($"/testing/e2e-scenarios/{runId:D}");
        Assert.That(cleanupResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var cleanup = await cleanupResponse.Content.ReadFromJsonAsync<E2eScenarioCleanupResponse>();
        Assert.That(cleanup, Is.Not.Null);
        Assert.That(cleanup!.DeletedProjectCount, Is.EqualTo(1));

        var secondCleanupResponse = await client.DeleteAsync($"/testing/e2e-scenarios/{runId:D}");
        Assert.That(secondCleanupResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var secondCleanup = await secondCleanupResponse.Content.ReadFromJsonAsync<E2eScenarioCleanupResponse>();
        Assert.That(secondCleanup, Is.Not.Null);
        Assert.That(secondCleanup!.DeletedProjectCount, Is.EqualTo(0));
    }

    [Test]
    public async Task Production_DoesNotMapScenarioEndpoints()
    {
        var response = await CreateClient(_productionFactory).PostAsJsonAsync(
            "/testing/e2e-scenarios/",
            new E2eScenarioSeedRequest(Guid.NewGuid()));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    private HttpClient CreateAuthorizedClient()
    {
        var client = CreateClient(_testingFactory);
        client.DefaultRequestHeaders.Add(E2eScenarioEndpoints.TokenHeaderName, ScenarioToken);
        return client;
    }

    private static HttpClient CreateClient(WebApplicationFactory<ServerEntryPoint> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static WebApplicationFactory<ServerEntryPoint> CreateFactory(
        string environment,
        bool useTestScheme,
        string scenarioToken,
        string databaseName) =>
        new WebApplicationFactory<ServerEntryPoint>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.UseSetting("Authentication:UseTestScheme", useTestScheme.ToString());
            builder.UseSetting("Testing:E2eScenario:AuthorizationToken", scenarioToken);
            builder.UseSetting("Authentication:Microsoft:TenantId", "00000000-0000-0000-0000-000000000000");
            builder.UseSetting("Authentication:Microsoft:ClientId", "00000000-0000-0000-0000-000000000001");
            builder.UseSetting("Authentication:Microsoft:ClientSecret", "not-used-in-tests");
            builder.UseSetting(
                "ConnectionStrings:DefaultConnection",
                "Server=(localdb)\\MSSQLLocalDB;Database=PlanDeckE2eScenarioTest;Trusted_Connection=True;");

            builder.ConfigureServices(services =>
            {
                var toRemove = services
                    .Where(descriptor =>
                        descriptor.ServiceType == typeof(DbContextOptions<PlanDeckDbContext>)
                        || descriptor.ServiceType == typeof(DbContextOptions)
                        || descriptor.ServiceType == typeof(PlanDeckDbContext)
                        || (descriptor.ServiceType.IsGenericType
                            && descriptor.ServiceType.GetGenericTypeDefinition().Name
                                == "IDbContextOptionsConfiguration`1"))
                    .ToList();

                foreach (var descriptor in toRemove)
                {
                    services.Remove(descriptor);
                }

                services.AddDbContext<PlanDeckDbContext>(options =>
                    options.UseInMemoryDatabase(databaseName));
            });
        });
}
