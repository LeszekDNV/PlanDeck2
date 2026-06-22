using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
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
using PlanDeck.Server.Hubs;
using PlanDeck.Server.Identity;

// Deliberately NOT under PlanDeck.Integration.Tests: the AspireAppFixture [SetUpFixture]
// lives in that namespace and would boot Aspire (Podman) for any enclosed test. These hub
// tests run against an in-memory WebApplicationFactory and must stay out of that scope so a
// filtered run never spins up the distributed app.
namespace PlanDeck.Realtime.IntegrationTests;

[TestFixture]
public sealed class PlanningRoomHubTests
{
    private const string TestTenantId = "11111111-1111-1111-1111-111111111111";
    private const string TestObjectId = "22222222-2222-2222-2222-222222222222";
    private const string TestEmail = "test.user@plandeck.local";

    private WebApplicationFactory<ServerEntryPoint> _factory = null!;
    private string _databaseName = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _databaseName = $"PlanDeckHubTests-{Guid.NewGuid()}";

        _factory = new WebApplicationFactory<ServerEntryPoint>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Authentication:UseTestScheme", "true");
            builder.UseSetting(
                "ConnectionStrings:DefaultConnection",
                "Server=(localdb)\\MSSQLLocalDB;Database=PlanDeckHubTest;Trusted_Connection=True;");

            // Swap the SqlServer context for an isolated in-memory store so the transport test
            // exercises the real seeding/persistence path without a SQL Server.
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
    public void Hub_RequiresAuthorization()
    {
        var attributes = typeof(PlanningRoomHub)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true);

        Assert.That(attributes, Is.Not.Empty, "PlanningRoomHub must be annotated with [Authorize].");
    }

    [Test]
    public async Task NonAssignedMember_IsRejected_OnJoin()
    {
        var (sessionId, _, _) = SeedSession(assignTestUser: false);

        var connection = CreateConnection();
        await connection.StartAsync();

        try
        {
            var ex = Assert.ThrowsAsync<HubException>(
                async () => await connection.InvokeAsync("JoinRoom", sessionId.ToString()));
            Assert.That(ex!.Message, Does.Contain("assigned member"));
        }
        finally
        {
            await connection.DisposeAsync();
        }
    }

    [Test]
    public async Task PerTaskFlow_HidesVotesUntilReveal_AndPersistsSelectedEstimate()
    {
        var (sessionId, firstTaskId, secondTaskId) = SeedSession(assignTestUser: true);
        var sid = sessionId.ToString();

        var emittedBeforeReveal = new ConcurrentQueue<PlanningRoomState>();
        var connection = CreateConnection();
        PlanningRoomState? latest = null;
        var signal = new SemaphoreSlim(0);
        connection.On<PlanningRoomState>("RoomStateChanged", state =>
        {
            latest = state;
            if (!state.IsRevealed)
            {
                emittedBeforeReveal.Enqueue(state);
            }

            signal.Release();
        });

        await connection.StartAsync();

        await connection.InvokeAsync("JoinRoom", sid);
        await WaitForBroadcastAsync(signal);

        // Seeded room activates the first task by sort order.
        Assert.That(latest!.CurrentTaskId, Is.EqualTo(firstTaskId));
        Assert.That(latest.Tasks.Select(task => task.TaskId), Is.EquivalentTo(new[] { firstTaskId, secondTaskId }));
        Assert.That(latest.ScaleValues, Is.EqualTo(new[] { "1", "2", "3", "5" }));

        await connection.InvokeAsync("SetActiveTask", sid, secondTaskId.ToString());
        await WaitForBroadcastAsync(signal);
        Assert.That(latest!.CurrentTaskId, Is.EqualTo(secondTaskId));

        await connection.InvokeAsync("CastVote", sid, "5");
        await WaitForBroadcastAsync(signal);

        Assert.That(emittedBeforeReveal, Is.Not.Empty);
        Assert.That(
            emittedBeforeReveal.SelectMany(state => state.Participants).All(participant => participant.Vote is null),
            Is.True,
            "No vote value may cross the wire before reveal.");
        Assert.That(latest!.Participants.Single().HasVoted, Is.True);

        await connection.InvokeAsync("RevealVotes", sid);
        await WaitForBroadcastAsync(signal);
        Assert.That(latest!.IsRevealed, Is.True);
        Assert.That(latest.Participants.Single().Vote, Is.EqualTo("5"));

        await connection.InvokeAsync("SelectEstimate", sid, secondTaskId.ToString(), "5");
        await WaitForBroadcastAsync(signal);

        var activeTask = latest!.Tasks.Single(task => task.TaskId == secondTaskId);
        Assert.That(activeTask.AgreedEstimate, Is.EqualTo("5"), "Estimate must be broadcast on the task.");

        await connection.DisposeAsync();

        Assert.That(
            ReadPersistedEstimate(sessionId, secondTaskId),
            Is.EqualTo("5"),
            "Selected estimate must be persisted to the database.");
    }

    private (Guid SessionId, Guid FirstTaskId, Guid SecondTaskId) SeedSession(bool assignTestUser)
    {
        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<RequestPrincipalAccessor>().Principal = BuildTestPrincipal();
        var db = scope.ServiceProvider.GetRequiredService<PlanDeckDbContext>();

        var sessionId = Guid.NewGuid();
        var firstTaskId = Guid.NewGuid();
        var secondTaskId = Guid.NewGuid();

        db.Sessions.Add(new PlanningSession
        {
            Id = sessionId,
            Name = "Hub Test Session",
            Status = SessionStatus.Active,
            ScaleType = VotingScaleType.Custom,
            ScaleValues = ["1", "2", "3", "5"]
        });

        db.SessionTasks.Add(new SessionTask { Id = firstTaskId, SessionId = sessionId, Title = "Task A", SortOrder = 0 });
        db.SessionTasks.Add(new SessionTask { Id = secondTaskId, SessionId = sessionId, Title = "Task B", SortOrder = 1 });

        if (assignTestUser)
        {
            db.SessionMembers.Add(new SessionMember { SessionId = sessionId, Email = TestEmail });
        }

        db.SaveChanges();
        return (sessionId, firstTaskId, secondTaskId);
    }

    private string? ReadPersistedEstimate(Guid sessionId, Guid taskId)
    {
        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<RequestPrincipalAccessor>().Principal = BuildTestPrincipal();
        var db = scope.ServiceProvider.GetRequiredService<PlanDeckDbContext>();
        return db.SessionTasks
            .AsNoTracking()
            .Single(task => task.Id == taskId && task.SessionId == sessionId)
            .AgreedEstimate;
    }

    private static ClaimsPrincipal BuildTestPrincipal()
    {
        var identity = new ClaimsIdentity(
        [
            new Claim("tid", TestTenantId),
            new Claim("oid", TestObjectId),
            new Claim("email", TestEmail)
        ], "Test");

        return new ClaimsPrincipal(identity);
    }

    private HubConnection CreateConnection()
    {
        return new HubConnectionBuilder()
            .WithUrl(new Uri(_factory.Server.BaseAddress, "hubs/planning-room"), options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
            })
            .Build();
    }

    private static async Task WaitForBroadcastAsync(SemaphoreSlim signal)
    {
        var entered = await signal.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.That(entered, Is.True, "Timed out waiting for a RoomStateChanged broadcast.");
    }
}
