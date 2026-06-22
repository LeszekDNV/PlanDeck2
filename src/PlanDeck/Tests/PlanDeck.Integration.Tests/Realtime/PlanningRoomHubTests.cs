using System.Collections.Concurrent;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using PlanDeck.Core.Shared.Realtime;
using PlanDeck.Server;
using PlanDeck.Server.Hubs;

// Deliberately NOT under PlanDeck.Integration.Tests: the AspireAppFixture [SetUpFixture]
// lives in that namespace and would boot Aspire (Podman) for any enclosed test. These hub
// tests run against an in-memory WebApplicationFactory and must stay out of that scope so a
// filtered run never spins up the distributed app.
namespace PlanDeck.Realtime.IntegrationTests;

[TestFixture]
public sealed class PlanningRoomHubTests
{
    private WebApplicationFactory<ServerEntryPoint> _factory = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _factory = new WebApplicationFactory<ServerEntryPoint>().WithWebHostBuilder(builder =>
        {
            // Non-Development environment skips the startup EF migration (Development-only),
            // so no real SQL Server is required for the hub transport test.
            builder.UseEnvironment("Testing");
            builder.UseSetting("Authentication:UseTestScheme", "true");
            builder.UseSetting(
                "ConnectionStrings:DefaultConnection",
                "Server=(localdb)\\MSSQLLocalDB;Database=PlanDeckHubTest;Trusted_Connection=True;");
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
    [Ignore("Phase 2 (S-06) makes the room per-task; CastVote now requires a seeded active task. " +
        "The hub gains DB-seeding in Phase 3, where this lifecycle test is rewritten for the per-task flow " +
        "(seed via DB -> set active task -> cast -> reveal -> pick).")]
    public async Task Lifecycle_HidesVotesUntilReveal_AndSurvivesReconnect()
    {
        var sessionId = Guid.NewGuid().ToString();
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
        await connection.InvokeAsync("JoinRoom", sessionId);
        await WaitForBroadcastAsync(signal);

        await connection.InvokeAsync("CastVote", sessionId, "5");
        await WaitForBroadcastAsync(signal);

        Assert.That(emittedBeforeReveal, Is.Not.Empty);
        Assert.That(
            emittedBeforeReveal.SelectMany(state => state.Participants).All(participant => participant.Vote is null),
            Is.True,
            "No vote value may cross the wire before reveal.");
        Assert.That(latest!.Participants.Single().HasVoted, Is.True);

        // Drop the connection: server OnDisconnectedAsync retains the participant + vote.
        await connection.DisposeAsync();

        var reconnected = CreateConnection();
        PlanningRoomState? afterRejoin = null;
        var rejoinSignal = new SemaphoreSlim(0);
        reconnected.On<PlanningRoomState>("RoomStateChanged", state =>
        {
            afterRejoin = state;
            rejoinSignal.Release();
        });

        await reconnected.StartAsync();
        await reconnected.InvokeAsync("JoinRoom", sessionId);
        await WaitForBroadcastAsync(rejoinSignal);

        var participant = afterRejoin!.Participants.Single();
        Assert.That(participant.IsOnline, Is.True, "Reconnected participant must be online.");
        Assert.That(participant.HasVoted, Is.True, "Vote must survive the reconnect.");
        Assert.That(participant.Vote, Is.Null, "Vote stays hidden until reveal.");

        await reconnected.InvokeAsync("RevealVotes", sessionId);
        await WaitForBroadcastAsync(rejoinSignal);

        Assert.That(afterRejoin!.IsRevealed, Is.True);
        Assert.That(afterRejoin.Participants.Single().Vote, Is.EqualTo("5"));

        await reconnected.DisposeAsync();
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
