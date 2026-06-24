using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PlanDeck.Application.Abstractions;
using PlanDeck.Application.Domain;
using PlanDeck.Infrastructure.Persistence;
using PlanDeck.Server;
using PlanDeck.Server.Identity;

// Deliberately NOT under PlanDeck.Integration.Tests: the AspireAppFixture [SetUpFixture] lives in
// that namespace and would boot Aspire (Podman). These endpoint tests run against an in-memory
// WebApplicationFactory and must stay out of that scope.
namespace PlanDeck.GuestJoin.IntegrationTests;

[TestFixture]
public sealed class GuestJoinEndpointTests
{
    private const string TestTenantId = "11111111-1111-1111-1111-111111111111";

    private WebApplicationFactory<ServerEntryPoint> _factory = null!;
    private string _databaseName = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _databaseName = $"PlanDeckGuestJoinTests-{Guid.NewGuid()}";

        _factory = new WebApplicationFactory<ServerEntryPoint>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Authentication:UseTestScheme", "true");
            builder.UseSetting(
                "ConnectionStrings:DefaultConnection",
                "Server=(localdb)\\MSSQLLocalDB;Database=PlanDeckGuestJoinTest;Trusted_Connection=True;");

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
                    options.UseInMemoryDatabase(_databaseName));
            });
        });
    }

    [OneTimeTearDown]
    public void OneTimeTearDown() => _factory.Dispose();

    [Test]
    public async Task GuestJoin_WithActiveCode_SetsGuestCookie_AndReturnsSessionId()
    {
        var sessionId = SeedSession(SessionStatus.Active, "ACTIVECODE1");
        var client = CreateClient();

        var response = await client.PostAsJsonAsync(
            "/guest/join", new { code = "ACTIVECODE1", displayName = "Alice" });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var payload = await response.Content.ReadFromJsonAsync<GuestJoinResponse>();
        Assert.That(payload, Is.Not.Null);
        Assert.That(payload!.SessionId, Is.EqualTo(sessionId));
        Assert.That(GetSetCookies(response), Has.Some.Contains(GuestAuthentication.CookieName));
    }

    [Test]
    public async Task GuestJoin_TrimsPaddedName_AndSucceeds()
    {
        SeedSession(SessionStatus.Active, "ACTIVECODE2");
        var client = CreateClient();

        var response = await client.PostAsJsonAsync(
            "/guest/join", new { code = "  ACTIVECODE2  ", displayName = "  Bob  " });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task GuestJoin_WithUnknownCode_Returns404_NoCookie()
    {
        var client = CreateClient();

        var response = await client.PostAsJsonAsync(
            "/guest/join", new { code = "NOSUCHCODE9", displayName = "Alice" });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(GetSetCookies(response), Has.None.Contains(GuestAuthentication.CookieName));
    }

    [Test]
    public async Task GuestJoin_WithDraftCode_Returns409_NoCookie()
    {
        SeedSession(SessionStatus.Draft, "DRAFTCODE1");
        var client = CreateClient();

        var response = await client.PostAsJsonAsync(
            "/guest/join", new { code = "DRAFTCODE1", displayName = "Alice" });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That(GetSetCookies(response), Has.None.Contains(GuestAuthentication.CookieName));
    }

    [Test]
    public async Task GuestJoin_WithEmptyName_Returns400_NoCookie()
    {
        SeedSession(SessionStatus.Active, "ACTIVECODE3");
        var client = CreateClient();

        var response = await client.PostAsJsonAsync(
            "/guest/join", new { code = "ACTIVECODE3", displayName = "   " });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(GetSetCookies(response), Has.None.Contains(GuestAuthentication.CookieName));
    }

    [Test]
    public async Task GuestJoin_WithTooLongName_Returns400_NoCookie()
    {
        SeedSession(SessionStatus.Active, "ACTIVECODE4");
        var client = CreateClient();

        var response = await client.PostAsJsonAsync(
            "/guest/join", new { code = "ACTIVECODE4", displayName = new string('x', 41) });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(GetSetCookies(response), Has.None.Contains(GuestAuthentication.CookieName));
    }

    [Test]
    public void GuestPrincipal_ResolvesIsGuest_AndParticipantId()
    {
        using var scope = _factory.Services.CreateScope();
        var participantId = Guid.NewGuid();
        scope.ServiceProvider.GetRequiredService<RequestPrincipalAccessor>().Principal =
            GuestAuthentication.BuildPrincipal(participantId, Guid.NewGuid(), "Alice", Guid.NewGuid());

        var context = scope.ServiceProvider.GetRequiredService<ICurrentUserContext>();

        Assert.Multiple(() =>
        {
            Assert.That(context.IsGuest, Is.True);
            Assert.That(context.ParticipantId, Is.EqualTo(participantId.ToString()));
            Assert.That(context.DisplayName, Is.EqualTo("Alice"));
        });
    }

    private Guid SeedSession(SessionStatus status, string shareCode)
    {
        using var scope = _factory.Services.CreateScope();
        // A tenant-bearing principal is required so the DbContext's tenant guard accepts the write.
        scope.ServiceProvider.GetRequiredService<RequestPrincipalAccessor>().Principal =
            GuestAuthentication.BuildPrincipal(Guid.NewGuid(), Guid.Parse(TestTenantId), "Seeder", Guid.NewGuid());
        var db = scope.ServiceProvider.GetRequiredService<PlanDeckDbContext>();

        var session = new PlanningSession
        {
            Id = Guid.NewGuid(),
            Name = "Guest Join Session",
            Status = status,
            ScaleType = VotingScaleType.Custom,
            ScaleValues = ["1", "2", "3", "5"],
            ShareCode = shareCode
        };

        db.Sessions.Add(session);
        db.SaveChanges();
        return session.Id;
    }

    private HttpClient CreateClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static IEnumerable<string> GetSetCookies(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values) ? values : [];
}
