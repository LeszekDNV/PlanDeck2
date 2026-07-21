using Grpc.Core;
using Grpc.Net.Client;
using Grpc.Net.Client.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PlanDeck.Core.Shared.Contracts;
using PlanDeck.Infrastructure.Persistence;
using PlanDeck.Server;
using PlanDeck.Server.Identity;
using ProtoBuf.Grpc.Client;

// Outside PlanDeck.Integration.Tests so these in-process transport tests do not boot Aspire.
namespace PlanDeck.Identity.IntegrationTests;

[TestFixture]
public sealed class GrpcAuthenticationTests
{
    private WebApplicationFactory<ServerEntryPoint> _factory = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _factory = new WebApplicationFactory<ServerEntryPoint>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Authentication:UseTestScheme", "true");
            builder.UseSetting(
                "ConnectionStrings:DefaultConnection",
                "Server=(localdb)\\MSSQLLocalDB;Database=PlanDeckGrpcAuthTest;Trusted_Connection=True;");
            builder.ConfigureServices(services =>
            {
                var descriptors = services
                    .Where(descriptor =>
                        descriptor.ServiceType == typeof(DbContextOptions<PlanDeckDbContext>)
                        || descriptor.ServiceType == typeof(DbContextOptions)
                        || descriptor.ServiceType == typeof(PlanDeckDbContext)
                        || (descriptor.ServiceType.IsGenericType
                            && descriptor.ServiceType.GetGenericTypeDefinition().Name
                                == "IDbContextOptionsConfiguration`1"))
                    .ToList();

                foreach (var descriptor in descriptors)
                {
                    services.Remove(descriptor);
                }

                services.AddDbContext<PlanDeckDbContext>(options =>
                    options.UseInMemoryDatabase($"GrpcAuthentication-{Guid.NewGuid():N}"));
            });
        });
    }

    [OneTimeTearDown]
    public void OneTimeTearDown() => _factory.Dispose();

    [TestCase("anonymous")]
    [TestCase("malformed")]
    public void ProtectedGrpcService_RejectsInvalidIdentityAsUnauthenticated(string identityShape)
    {
        using var channel = CreateChannel(identityShape);
        var service = channel.CreateGrpcService<ITeamService>();

        var exception = Assert.ThrowsAsync<RpcException>(
            async () => await service.ListTeamsAsync(new ListTeamsRequest()));

        Assert.That(exception!.StatusCode, Is.EqualTo(StatusCode.Unauthenticated));
    }

    [Test]
    public async Task ProtectedGrpcService_AllowsValidMember()
    {
        using var channel = CreateChannel();
        var service = channel.CreateGrpcService<ITeamService>();

        var reply = await service.ListTeamsAsync(new ListTeamsRequest());

        Assert.That(reply.Teams, Is.Empty);
    }

    [Test]
    public async Task AnonymousAuthRpc_RemainsAvailable()
    {
        using var channel = CreateChannel("anonymous");
        var service = channel.CreateGrpcService<IAuthService>();

        var reply = await service.GetCurrentUserAsync(new CurrentUserRequest());

        Assert.That(reply.IsAuthenticated, Is.False);
    }

    [Test]
    public void Guest_CannotReachMemberService()
    {
        using var channel = CreateChannel(
            identityShape: null,
            guestSessionId: Guid.NewGuid());
        var service = channel.CreateGrpcService<ITeamService>();

        var exception = Assert.ThrowsAsync<RpcException>(
            async () => await service.ListTeamsAsync(new ListTeamsRequest()));

        Assert.That(exception!.StatusCode, Is.EqualTo(StatusCode.PermissionDenied));
    }

    private GrpcChannel CreateChannel(
        string? identityShape = null,
        Guid? guestSessionId = null)
    {
        var httpClient = _factory.CreateDefaultClient(new GrpcWebHandler(
            GrpcWebMode.GrpcWeb,
            _factory.Server.CreateHandler()));
        if (identityShape is not null)
        {
            httpClient.DefaultRequestHeaders.Add(
                TestAuthenticationHandler.IdentityShapeHeader,
                identityShape);
        }

        if (guestSessionId is not null)
        {
            httpClient.DefaultRequestHeaders.Add(
                TestAuthenticationHandler.GuestSessionHeader,
                guestSessionId.Value.ToString());
        }

        return GrpcChannel.ForAddress(
            httpClient.BaseAddress!,
            new GrpcChannelOptions { HttpClient = httpClient });
    }
}
