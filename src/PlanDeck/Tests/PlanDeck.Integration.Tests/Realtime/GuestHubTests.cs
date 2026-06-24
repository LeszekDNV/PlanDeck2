using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PlanDeck.Application.Domain;
using PlanDeck.Core.Shared.Realtime;
using PlanDeck.Infrastructure.Persistence;
using PlanDeck.Server;
using PlanDeck.Server.Identity;

// Outside PlanDeck.Integration.Tests so the AspireAppFixture [SetUpFixture] never boots Aspire.
namespace PlanDeck.Realtime.IntegrationTests;

[TestFixture]
public sealed class GuestHubTests
{
    private const string GuestSessionHeader = "X-Test-Guest-Sid";
    private const string TestTenantId = "11111111-1111-1111-1111-111111111111";
    private const string GuestObjectId = "44444444-4444-4444-4444-444444444444";

    private WebApplicationFactory<ServerEntryPoint> _factory = null!;
    private string _databaseName = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _databaseName = $"PlanDeckGuestHubTests-{Guid.NewGuid()}";

        _factory = new WebApplicationFactory<ServerEntryPoint>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Authentication:UseTestScheme", "true");
            builder.UseSetting(
                "ConnectionStrings:DefaultConnection",
                "Server=(localdb)\\MSSQLLocalDB;Database=PlanDeckGuestHubTest;Trusted_Connection=True;");

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
    public async Task GuestJoin_ToActiveSession_YieldsStateWithGuestParticipant()
    {
        var sessionId = SeedActiveSession();
        var connection = CreateGuestConnection(sessionId);
        PlanningRoomState? latest = null;
        var signal = new SemaphoreSlim(0);
        connection.On<PlanningRoomState>("RoomStateChanged", state =>
        {
            latest = state;
            signal.Release();
        });

        await connection.StartAsync();
        try
        {
            await connection.InvokeAsync("JoinRoom", sessionId.ToString());
            await WaitForBroadcastAsync(signal);

            Assert.That(latest, Is.Not.Null);
            Assert.That(latest!.Participants.Select(p => p.ParticipantId), Does.Contain(GuestObjectId));
        }
        finally
        {
            await connection.DisposeAsync();
        }
    }

    [Test]
    public async Task Guest_WithMismatchedSid_IsRejectedByScopeGuard()
    {
        var sessionId = SeedActiveSession();
        var connection = CreateGuestConnection(sessionId);
        await connection.StartAsync();

        try
        {
            var ex = Assert.ThrowsAsync<HubException>(
                async () => await connection.InvokeAsync("JoinRoom", Guid.NewGuid().ToString()));
            Assert.That(ex!.Message, Does.Contain("not valid for the requested session"));
        }
        finally
        {
            await connection.DisposeAsync();
        }
    }

    [Test]
    public async Task Guest_CastVote_Succeeds()
    {
        var sessionId = SeedActiveSession();
        var connection = CreateGuestConnection(sessionId);
        PlanningRoomState? latest = null;
        var signal = new SemaphoreSlim(0);
        connection.On<PlanningRoomState>("RoomStateChanged", state =>
        {
            latest = state;
            signal.Release();
        });

        await connection.StartAsync();
        try
        {
            await connection.InvokeAsync("JoinRoom", sessionId.ToString());
            await WaitForBroadcastAsync(signal);

            await connection.InvokeAsync("CastVote", sessionId.ToString(), "5");
            await WaitForBroadcastAsync(signal);

            var guest = latest!.Participants.Single(p => p.ParticipantId == GuestObjectId);
            Assert.That(guest.HasVoted, Is.True);
        }
        finally
        {
            await connection.DisposeAsync();
        }
    }

    [Test]
    public async Task Guest_ControlActions_AreRejected()
    {
        var sessionId = SeedActiveSession();
        var connection = CreateGuestConnection(sessionId);
        await connection.StartAsync();

        try
        {
            await connection.InvokeAsync("JoinRoom", sessionId.ToString());
            var sid = sessionId.ToString();
            var taskId = Guid.NewGuid().ToString();

            Assert.Multiple(() =>
            {
                Assert.ThrowsAsync<HubException>(async () => await connection.InvokeAsync("RevealVotes", sid));
                Assert.ThrowsAsync<HubException>(async () => await connection.InvokeAsync("ResetRound", sid));
                Assert.ThrowsAsync<HubException>(async () => await connection.InvokeAsync("SetActiveTask", sid, taskId));
                Assert.ThrowsAsync<HubException>(async () => await connection.InvokeAsync("SelectEstimate", sid, taskId, "5"));
            });
        }
        finally
        {
            await connection.DisposeAsync();
        }
    }

    private Guid SeedActiveSession()
    {
        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<RequestPrincipalAccessor>().Principal =
            GuestAuthentication.BuildPrincipal(Guid.NewGuid(), Guid.Parse(TestTenantId), "Seeder", Guid.NewGuid());
        var db = scope.ServiceProvider.GetRequiredService<PlanDeckDbContext>();

        var sessionId = Guid.NewGuid();
        db.Sessions.Add(new PlanningSession
        {
            Id = sessionId,
            Name = "Guest Hub Session",
            Status = SessionStatus.Active,
            ScaleType = VotingScaleType.Custom,
            ScaleValues = ["1", "2", "3", "5"]
        });
        db.SessionTasks.Add(new SessionTask { Id = Guid.NewGuid(), SessionId = sessionId, Title = "Task A", SortOrder = 0 });
        db.SaveChanges();
        return sessionId;
    }

    private HubConnection CreateGuestConnection(Guid sessionId)
    {
        return new HubConnectionBuilder()
            .WithUrl(new Uri(_factory.Server.BaseAddress, "hubs/planning-room"), options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
                options.Headers.Add(GuestSessionHeader, sessionId.ToString());
            })
            .Build();
    }

    private static async Task WaitForBroadcastAsync(SemaphoreSlim signal)
    {
        var entered = await signal.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.That(entered, Is.True, "Timed out waiting for a RoomStateChanged broadcast.");
    }
}
