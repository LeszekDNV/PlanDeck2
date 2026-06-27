using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Http.Connections.Client;
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
    public async Task Creator_IsAuthorized_WithoutExplicitMembership()
    {
        // Session created by the connecting user (oid), with no SessionMember row for them.
        var (sessionId, _, _) = SeedSession(assignTestUser: false, createdByUserId: Guid.Parse(TestObjectId));

        var connection = CreateConnection();
        await connection.StartAsync();

        try
        {
            Assert.DoesNotThrowAsync(
                async () => await connection.InvokeAsync("JoinRoom", sessionId.ToString()));
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

    [Test]
    public async Task Reconnect_AfterDisconnect_RetainsVoteAndRestoresOnlineStatus()
    {
        var (sessionId, _, _) = SeedSession(assignTestUser: true);
        var sid = sessionId.ToString();

        var first = CreateConnection();
        PlanningRoomState? latest = null;
        var firstSignal = new SemaphoreSlim(0);
        first.On<PlanningRoomState>("RoomStateChanged", state =>
        {
            latest = state;
            firstSignal.Release();
        });

        await first.StartAsync();
        await first.InvokeAsync("JoinRoom", sid);
        await WaitForBroadcastAsync(firstSignal);

        await first.InvokeAsync("CastVote", sid, "3");
        await WaitForBroadcastAsync(firstSignal);

        var beforeDisconnect = latest!.Participants.Single(participant => participant.ParticipantId == TestObjectId);
        Assert.That(beforeDisconnect.HasVoted, Is.True);
        Assert.That(beforeDisconnect.IsOnline, Is.True);

        await first.DisposeAsync();

        var second = CreateConnection();
        PlanningRoomState? rejoined = null;
        var secondSignal = new SemaphoreSlim(0);
        second.On<PlanningRoomState>("RoomStateChanged", state =>
        {
            rejoined = state;
            secondSignal.Release();
        });

        await second.StartAsync();
        await second.InvokeAsync("JoinRoom", sid);
        await WaitForBroadcastAsync(secondSignal);

        var afterReconnect = rejoined!.Participants.Single(participant => participant.ParticipantId == TestObjectId);
        Assert.That(afterReconnect.HasVoted, Is.True, "Disconnect should not drop the voted flag.");
        Assert.That(afterReconnect.IsOnline, Is.True, "Rejoin should restore online presence.");
        Assert.That(afterReconnect.Vote, Is.Null, "Votes remain hidden until reveal.");

        await second.DisposeAsync();
    }

    [Test]
    public async Task Disconnect_WithoutLeave_KeepsVote_AndMarksParticipantOffline()
    {
        var (sessionId, _, _) = SeedSession(assignTestUser: true);
        var sid = sessionId.ToString();

        var observer = CreateGuestConnection(sessionId);
        PlanningRoomState? observerState = null;
        var observerSignal = new SemaphoreSlim(0);
        observer.On<PlanningRoomState>("RoomStateChanged", state =>
        {
            observerState = state;
            observerSignal.Release();
        });

        await observer.StartAsync();
        await observer.InvokeAsync("JoinRoom", sid);
        await WaitForBroadcastAsync(observerSignal);

        var voter = CreateConnection();
        await voter.StartAsync();
        await voter.InvokeAsync("JoinRoom", sid);
        await WaitForBroadcastAsync(observerSignal);

        await voter.InvokeAsync("CastVote", sid, "5");
        await WaitForBroadcastAsync(observerSignal);

        var beforeDisconnect = observerState!.Participants.Single(participant => participant.ParticipantId == TestObjectId);
        Assert.That(beforeDisconnect.HasVoted, Is.True);
        Assert.That(beforeDisconnect.IsOnline, Is.True);

        await voter.DisposeAsync();
        await WaitForBroadcastAsync(observerSignal);

        var afterDisconnect = observerState!.Participants.Single(participant => participant.ParticipantId == TestObjectId);
        Assert.That(afterDisconnect.HasVoted, Is.True, "Disconnect should not clear vote state.");
        Assert.That(afterDisconnect.IsOnline, Is.False, "Disconnect should mark participant offline.");

        await observer.DisposeAsync();
    }

    [Test]
    public async Task Reveal_WithPartialTurnout_ExposesOnlySubmittedVotes()
    {
        var (sessionId, _, _) = SeedSession(assignTestUser: true);
        var sid = sessionId.ToString();

        var member = CreateConnection();
        PlanningRoomState? latest = null;
        var signal = new SemaphoreSlim(0);
        member.On<PlanningRoomState>("RoomStateChanged", state =>
        {
            latest = state;
            signal.Release();
        });

        var guest = CreateGuestConnection(sessionId);
        await member.StartAsync();
        await member.InvokeAsync("JoinRoom", sid);
        await WaitForBroadcastAsync(signal);

        await guest.StartAsync();
        await guest.InvokeAsync("JoinRoom", sid);
        await WaitForBroadcastAsync(signal);

        await member.InvokeAsync("CastVote", sid, "5");
        await WaitForBroadcastAsync(signal);

        await member.InvokeAsync("RevealVotes", sid);
        await WaitForBroadcastAsync(signal);

        Assert.That(latest!.IsRevealed, Is.True);
        var memberState = latest.Participants.Single(participant => participant.ParticipantId == TestObjectId);
        Assert.That(memberState.Vote, Is.EqualTo("5"));

        var guestState = latest.Participants.Single(participant => participant.ParticipantId == "44444444-4444-4444-4444-444444444444");
        Assert.That(guestState.HasVoted, Is.False);
        Assert.That(guestState.Vote, Is.Null);

        await guest.DisposeAsync();
        await member.DisposeAsync();
    }

    [Test]
    public async Task Reveal_IsIdempotent_SecondCallReturnsSameState()
    {
        var (sessionId, _, _) = SeedSession(assignTestUser: true);
        var sid = sessionId.ToString();

        var connection = CreateConnection();
        var signal = new SemaphoreSlim(0);
        PlanningRoomState? latest = null;
        connection.On<PlanningRoomState>("RoomStateChanged", state =>
        {
            latest = state;
            signal.Release();
        });

        await connection.StartAsync();
        await connection.InvokeAsync("JoinRoom", sid);
        await WaitForBroadcastAsync(signal);
        await connection.InvokeAsync("CastVote", sid, "3");
        await WaitForBroadcastAsync(signal);

        await connection.InvokeAsync("RevealVotes", sid);
        await WaitForBroadcastAsync(signal);
        var firstReveal = latest!;

        await connection.InvokeAsync("RevealVotes", sid);
        await WaitForBroadcastAsync(signal);
        var secondReveal = latest!;

        Assert.That(secondReveal.IsRevealed, Is.EqualTo(firstReveal.IsRevealed));
        Assert.That(secondReveal.CurrentTaskId, Is.EqualTo(firstReveal.CurrentTaskId));
        Assert.That(secondReveal.Tasks, Is.EqualTo(firstReveal.Tasks));
        Assert.That(secondReveal.Participants, Is.EqualTo(firstReveal.Participants));
        Assert.That(secondReveal.ScaleValues, Is.EqualTo(firstReveal.ScaleValues));

        await connection.DisposeAsync();
    }

    [Test]
    public async Task CastVote_AfterReveal_ThrowsHubException()
    {
        var (sessionId, _, _) = SeedSession(assignTestUser: true);
        var sid = sessionId.ToString();
        var connection = CreateConnection();
        var signal = new SemaphoreSlim(0);
        connection.On<PlanningRoomState>("RoomStateChanged", _ => signal.Release());

        await connection.StartAsync();
        await connection.InvokeAsync("JoinRoom", sid);
        await WaitForBroadcastAsync(signal);
        await connection.InvokeAsync("CastVote", sid, "3");
        await WaitForBroadcastAsync(signal);
        await connection.InvokeAsync("RevealVotes", sid);
        await WaitForBroadcastAsync(signal);

        var ex = Assert.ThrowsAsync<HubException>(async () => await connection.InvokeAsync("CastVote", sid, "5"));
        Assert.That(ex, Is.Not.Null);

        var extraBroadcast = await signal.WaitAsync(TimeSpan.FromMilliseconds(300));
        Assert.That(extraBroadcast, Is.False, "Rejected post-reveal vote must not emit a RoomStateChanged broadcast.");

        await connection.DisposeAsync();
    }

    [Test]
    public async Task SelectEstimate_OnPersistFailure_DoesNotBroadcast()
    {
        var (sessionId, _, _) = SeedSession(assignTestUser: true);
        var sid = sessionId.ToString();
        var connection = CreateConnection();
        var signal = new SemaphoreSlim(0);
        connection.On<PlanningRoomState>("RoomStateChanged", _ => signal.Release());

        await connection.StartAsync();
        await connection.InvokeAsync("JoinRoom", sid);
        await WaitForBroadcastAsync(signal);

        var ex = Assert.ThrowsAsync<HubException>(async () =>
            await connection.InvokeAsync("SelectEstimate", sid, Guid.NewGuid().ToString(), "3"));
        Assert.That(ex!.Message, Does.Contain("could not be saved"));

        var extraBroadcast = await signal.WaitAsync(TimeSpan.FromMilliseconds(300));
        Assert.That(extraBroadcast, Is.False, "Failed persistence must not emit a RoomStateChanged broadcast.");

        await connection.DisposeAsync();
    }

    [Test]
    public async Task SelectEstimate_LastWriteWins_NoConcurrencyError()
    {
        var (sessionId, _, secondTaskId) = SeedSession(assignTestUser: true);
        var sid = sessionId.ToString();

        var connection = CreateConnection();
        PlanningRoomState? latest = null;
        var signal = new SemaphoreSlim(0);
        connection.On<PlanningRoomState>("RoomStateChanged", state =>
        {
            latest = state;
            signal.Release();
        });

        await connection.StartAsync();
        await connection.InvokeAsync("JoinRoom", sid);
        await WaitForBroadcastAsync(signal);
        await connection.InvokeAsync("SetActiveTask", sid, secondTaskId.ToString());
        await WaitForBroadcastAsync(signal);

        await connection.InvokeAsync("SelectEstimate", sid, secondTaskId.ToString(), "3");
        await WaitForBroadcastAsync(signal);
        await connection.InvokeAsync("SelectEstimate", sid, secondTaskId.ToString(), "5");
        await WaitForBroadcastAsync(signal);

        var taskState = latest!.Tasks.Single(task => task.TaskId == secondTaskId);
        Assert.That(taskState.AgreedEstimate, Is.EqualTo("5"));
        Assert.That(ReadPersistedEstimate(sessionId, secondTaskId), Is.EqualTo("5"));

        await connection.DisposeAsync();
    }

    [Test]
    public async Task JoinRoom_WithCustomScale_VoteOutsideScaleRejected()
    {
        var (sessionId, taskIds) = SeedSessionWithConfig(
            assignTestUser: true,
            scaleValues: ["S", "M", "L"],
            tasks: [("Task Custom Scale", 0)]);
        var sid = sessionId.ToString();
        var connection = CreateConnection();
        PlanningRoomState? latest = null;
        var signal = new SemaphoreSlim(0);
        connection.On<PlanningRoomState>("RoomStateChanged", state =>
        {
            latest = state;
            signal.Release();
        });

        await connection.StartAsync();
        await connection.InvokeAsync("JoinRoom", sid);
        await WaitForBroadcastAsync(signal);

        Assert.That(latest!.ScaleValues, Is.EqualTo(new[] { "S", "M", "L" }));
        Assert.That(latest.Tasks.Single().TaskId, Is.EqualTo(taskIds[0]));

        var ex = Assert.ThrowsAsync<HubException>(async () => await connection.InvokeAsync("CastVote", sid, "5"));
        Assert.That(ex, Is.Not.Null);

        await connection.DisposeAsync();
    }

    [Test]
    public async Task JoinRoom_WithMultipleTasks_TaskSelectionPreserved()
    {
        var (sessionId, taskIds) = SeedSessionWithConfig(
            assignTestUser: true,
            scaleValues: ["1", "2", "3", "5"],
            tasks: [("Task C", 2), ("Task A", 0), ("Task B", 1)]);
        var sid = sessionId.ToString();
        var connection = CreateConnection();
        PlanningRoomState? latest = null;
        var signal = new SemaphoreSlim(0);
        connection.On<PlanningRoomState>("RoomStateChanged", state =>
        {
            latest = state;
            signal.Release();
        });

        await connection.StartAsync();
        await connection.InvokeAsync("JoinRoom", sid);
        await WaitForBroadcastAsync(signal);

        Assert.That(latest!.Tasks.Select(task => task.SortOrder), Is.EqualTo(new[] { 0, 1, 2 }));
        Assert.That(latest.Tasks.Select(task => task.Title), Is.EqualTo(new[] { "Task A", "Task B", "Task C" }));
        Assert.That(latest.CurrentTaskId, Is.EqualTo(taskIds[1]), "Current task should be the lowest sort-order task.");

        await connection.DisposeAsync();
    }

    [Test]
    public async Task ConfigRoundTrip_CreateAndJoin_ScaleMatchesConfiguration()
    {
        var (sessionId, _) = SeedSessionWithConfig(
            assignTestUser: true,
            scaleValues: ["0", "1", "2", "3", "5", "8", "13", "21", "?", "☕"],
            tasks: [("Task A", 0), ("Task B", 1)]);
        var sid = sessionId.ToString();
        var connection = CreateConnection();
        PlanningRoomState? latest = null;
        var signal = new SemaphoreSlim(0);
        connection.On<PlanningRoomState>("RoomStateChanged", state =>
        {
            latest = state;
            signal.Release();
        });

        await connection.StartAsync();
        await connection.InvokeAsync("JoinRoom", sid);
        await WaitForBroadcastAsync(signal);

        Assert.That(
            latest!.ScaleValues,
            Is.EqualTo(new[] { "0", "1", "2", "3", "5", "8", "13", "21", "?", "☕" }),
            "Voting room scale should match persisted session configuration.");

        await connection.DisposeAsync();
    }

    private (Guid SessionId, Guid FirstTaskId, Guid SecondTaskId) SeedSession(bool assignTestUser, Guid? createdByUserId = null)
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
            ScaleValues = ["1", "2", "3", "5"],
            CreatedByUserId = createdByUserId ?? Guid.Empty
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

    private (Guid SessionId, Guid[] TaskIds) SeedSessionWithConfig(
        bool assignTestUser,
        IReadOnlyList<string> scaleValues,
        IReadOnlyList<(string Title, int SortOrder)> tasks,
        Guid? createdByUserId = null)
    {
        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<RequestPrincipalAccessor>().Principal = BuildTestPrincipal();
        var db = scope.ServiceProvider.GetRequiredService<PlanDeckDbContext>();

        var sessionId = Guid.NewGuid();
        var taskIds = tasks.Select(_ => Guid.NewGuid()).ToArray();
        db.Sessions.Add(new PlanningSession
        {
            Id = sessionId,
            Name = "Hub Config Session",
            Status = SessionStatus.Active,
            ScaleType = VotingScaleType.Custom,
            ScaleValues = [.. scaleValues],
            CreatedByUserId = createdByUserId ?? Guid.Empty
        });

        for (var i = 0; i < tasks.Count; i++)
        {
            var (title, sortOrder) = tasks[i];
            db.SessionTasks.Add(new SessionTask
            {
                Id = taskIds[i],
                SessionId = sessionId,
                Title = title,
                SortOrder = sortOrder
            });
        }

        if (assignTestUser)
        {
            db.SessionMembers.Add(new SessionMember { SessionId = sessionId, Email = TestEmail });
        }

        db.SaveChanges();
        return (sessionId, taskIds);
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

    private HubConnection CreateConnection(Action<HttpConnectionOptions>? configure = null)
    {
        return new HubConnectionBuilder()
            .WithUrl(new Uri(_factory.Server.BaseAddress, "hubs/planning-room"), options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
                configure?.Invoke(options);
            })
            .Build();
    }

    private HubConnection CreateGuestConnection(Guid sessionId)
    {
        return CreateConnection(options =>
            options.Headers[TestAuthenticationHandler.GuestSessionHeader] = sessionId.ToString());
    }

    private static async Task WaitForBroadcastAsync(SemaphoreSlim signal)
    {
        var entered = await signal.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.That(entered, Is.True, "Timed out waiting for a RoomStateChanged broadcast.");
    }
}
