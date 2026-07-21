using PlanDeck.Application.Abstractions;
using PlanDeck.Application.Planning;
using PlanDeck.Core.Shared.Realtime;
using System.Collections.Concurrent;

namespace PlanDeck.Unit.Tests.Planning;

[TestFixture]
public sealed class PlanningRoomServiceTests
{
    private static readonly string[] Scale = ["0", "1", "2", "3", "5", "8", "13", "21", "?", "\u2615"];

    private PlanningRoomService _service = null!;
    private RoomKey _key;
    private Guid _taskId;

    [SetUp]
    public void SetUp()
    {
        _service = new PlanningRoomService();
        _key = new RoomKey(Guid.NewGuid(), Guid.NewGuid());
        _taskId = Guid.NewGuid();
        _service.EnsureSeeded(_key, [Task(_taskId, "Task 1", 0)], Scale);
    }

    private static PlanningRoomTaskSnapshot Task(Guid id, string title, int sortOrder, string? agreedEstimate = null, string? description = null)
        => new(id, title, description, sortOrder, agreedEstimate);

    private static PlanningParticipantState Participant(PlanningRoomState state, string participantId)
    {
        return state.Participants.Single(p => p.ParticipantId == participantId);
    }

    [Test]
    public void CastVote_BeforeReveal_DoesNotExposeVoteValue()
    {
        _service.Join(_key, "alice", "Alice", "conn-a");
        _service.Join(_key, "bob", "Bob", "conn-b");

        _service.CastVote(_key, "alice", "5");
        var state = _service.CastVote(_key, "bob", "8");

        Assert.That(state.IsRevealed, Is.False);
        Assert.That(state.Participants.All(p => p.Vote is null), Is.True);
        Assert.That(Participant(state, "alice").HasVoted, Is.True);
        Assert.That(Participant(state, "bob").HasVoted, Is.True);
    }

    [Test]
    public void RevealVotes_ExposesEveryCastVote()
    {
        _service.Join(_key, "alice", "Alice", "conn-a");
        _service.Join(_key, "bob", "Bob", "conn-b");
        _service.CastVote(_key, "alice", "5");
        _service.CastVote(_key, "bob", "8");

        var state = _service.RevealVotes(_key);

        Assert.That(state.IsRevealed, Is.True);
        Assert.That(Participant(state, "alice").Vote, Is.EqualTo("5"));
        Assert.That(Participant(state, "bob").Vote, Is.EqualTo("8"));
    }

    [Test]
    public void RevealVotes_IsIdempotent_AndWorksWithPartialTurnout()
    {
        _service.Join(_key, "alice", "Alice", "conn-a");
        _service.Join(_key, "bob", "Bob", "conn-b");
        _service.CastVote(_key, "alice", "5");

        _service.RevealVotes(_key);
        var state = _service.RevealVotes(_key);

        Assert.That(state.IsRevealed, Is.True);
        Assert.That(Participant(state, "alice").Vote, Is.EqualTo("5"));
        Assert.That(Participant(state, "bob").Vote, Is.Null);
        Assert.That(Participant(state, "bob").HasVoted, Is.False);
    }

    [Test]
    public void CastVote_AfterReveal_IsRejected()
    {
        _service.Join(_key, "alice", "Alice", "conn-a");
        _service.CastVote(_key, "alice", "5");
        _service.RevealVotes(_key);

        Assert.Throws<InvalidOperationException>(() => _service.CastVote(_key, "alice", "8"));
    }

    [Test]
    public void CastVote_WithoutJoining_IsRejected()
    {
        Assert.Throws<InvalidOperationException>(() => _service.CastVote(_key, "ghost", "5"));
    }

    [Test]
    public void CastVote_ChangeBeforeReveal_OverwritesWithoutDuplicating()
    {
        _service.Join(_key, "alice", "Alice", "conn-a");
        _service.CastVote(_key, "alice", "5");
        _service.CastVote(_key, "alice", "13");

        var state = _service.RevealVotes(_key);

        Assert.That(state.Participants, Has.Count.EqualTo(1));
        Assert.That(Participant(state, "alice").Vote, Is.EqualTo("13"));
    }

    [Test]
    public void ResetRound_ClearsVotesAndReHides()
    {
        _service.Join(_key, "alice", "Alice", "conn-a");
        _service.CastVote(_key, "alice", "5");
        _service.RevealVotes(_key);

        var state = _service.ResetRound(_key, _taskId);

        Assert.That(state.IsRevealed, Is.False);
        Assert.That(Participant(state, "alice").Vote, Is.Null);
        Assert.That(Participant(state, "alice").HasVoted, Is.False);
        Assert.That(Participant(state, "alice").IsOnline, Is.True);
    }

    [Test]
    public void Join_IsIdempotentPerParticipant_PreservingVote()
    {
        _service.Join(_key, "alice", "Alice", "conn-a1");
        _service.CastVote(_key, "alice", "5");

        var state = _service.Join(_key, "alice", "Alice", "conn-a2");

        Assert.That(state.Participants, Has.Count.EqualTo(1));
        Assert.That(Participant(state, "alice").HasVoted, Is.True);
        Assert.That(Participant(state, "alice").IsOnline, Is.True);
    }

    [Test]
    public void Disconnect_KeepsVote_AndFlipsOfflineOnlyOnLastConnection()
    {
        _service.Join(_key, "alice", "Alice", "conn-a1");
        _service.Join(_key, "alice", "Alice", "conn-a2");
        _service.CastVote(_key, "alice", "5");

        var afterFirst = _service.Disconnect("conn-a1");
        Assert.That(afterFirst, Is.Not.Null);
        Assert.That(Participant(afterFirst!.Value.State, "alice").IsOnline, Is.True);

        var afterLast = _service.Disconnect("conn-a2");
        Assert.That(afterLast, Is.Not.Null);
        Assert.That(Participant(afterLast!.Value.State, "alice").IsOnline, Is.False);

        var revealed = _service.RevealVotes(_key);
        Assert.That(Participant(revealed, "alice").Vote, Is.EqualTo("5"));
    }

    [Test]
    public void Disconnect_UnknownConnection_ReturnsNull()
    {
        Assert.That(_service.Disconnect("nope"), Is.Null);
    }

    [Test]
    public void Reconnect_RestoresOnline_WithVoteIntact()
    {
        _service.Join(_key, "alice", "Alice", "conn-a1");
        _service.CastVote(_key, "alice", "5");
        _service.Disconnect("conn-a1");

        var state = _service.Join(_key, "alice", "Alice", "conn-a2");

        Assert.That(Participant(state, "alice").IsOnline, Is.True);
        Assert.That(state.Participants, Has.Count.EqualTo(1));

        var revealed = _service.RevealVotes(_key);
        Assert.That(Participant(revealed, "alice").Vote, Is.EqualTo("5"));
    }

    [Test]
    public void Leave_RemovesParticipantOnLastConnection()
    {
        _service.Join(_key, "alice", "Alice", "conn-a");
        _service.Join(_key, "bob", "Bob", "conn-b");

        var state = _service.Leave(_key, "alice", "conn-a");

        Assert.That(state.Participants, Has.Count.EqualTo(1));
        Assert.That(state.Participants.Single().ParticipantId, Is.EqualTo("bob"));
        Assert.That(_service.Disconnect("conn-a"), Is.Null);
    }

    [Test]
    public void Leave_FromOneConnection_KeepsParticipantAndVoteWhileOtherConnectionLive()
    {
        _service.Join(_key, "alice", "Alice", "conn-a1");
        _service.Join(_key, "alice", "Alice", "conn-a2");
        _service.CastVote(_key, "alice", "5");

        var state = _service.Leave(_key, "alice", "conn-a1");

        var alice = state.Participants.Single(p => p.ParticipantId == "alice");
        Assert.That(alice.HasVoted, Is.True);
        Assert.That(alice.IsOnline, Is.True);

        var revealed = _service.RevealVotes(_key);
        Assert.That(revealed.Participants.Single(p => p.ParticipantId == "alice").Vote, Is.EqualTo("5"));
    }

    [Test]
    public void Rooms_AreIsolatedByTenantForSameSessionId()
    {
        var sessionId = Guid.NewGuid();
        var tenantA = new RoomKey(Guid.NewGuid(), sessionId);
        var tenantB = new RoomKey(Guid.NewGuid(), sessionId);

        _service.Join(tenantA, "alice", "Alice", "conn-a");
        var stateB = _service.Join(tenantB, "bob", "Bob", "conn-b");

        Assert.That(stateB.Participants, Has.Count.EqualTo(1));
        Assert.That(stateB.Participants.Single().ParticipantId, Is.EqualTo("bob"));
    }

    [Test]
    public void Join_WithConnectionOwnedByAnotherRoom_IsRejectedAndDisconnectUpdatesOriginalRoom()
    {
        var otherKey = new RoomKey(_key.TenantId, Guid.NewGuid());
        _service.EnsureSeeded(otherKey, [Task(Guid.NewGuid(), "Other task", 0)], Scale);
        _service.Join(_key, "alice", "Alice", "conn-a");

        var exception = Assert.Throws<InvalidOperationException>(
            () => _service.Join(otherKey, "alice", "Alice", "conn-a"));

        Assert.That(exception!.Message, Does.Contain("different planning room"));
        Assert.That(_service.GetState(otherKey).Participants, Is.Empty);

        var disconnected = _service.Disconnect("conn-a");

        Assert.That(disconnected, Is.Not.Null);
        Assert.That(disconnected!.Value.Key, Is.EqualTo(_key));
        Assert.That(Participant(disconnected.Value.State, "alice").IsOnline, Is.False);
    }

    [Test]
    public void RemoveInactiveRooms_RemovesRoomOnlyAfterFifteenMinutes()
    {
        var clock = new ManualTimeProvider();
        var service = new PlanningRoomService(clock);
        var key = new RoomKey(Guid.NewGuid(), Guid.NewGuid());
        var taskId = Guid.NewGuid();
        service.EnsureSeeded(key, [Task(taskId, "Task", 0)], Scale);
        service.Join(key, "alice", "Alice", "conn-a");
        service.Disconnect("conn-a");

        clock.Advance(TimeSpan.FromMinutes(14));
        Assert.That(service.RemoveInactiveRooms(clock.GetUtcNow() - TimeSpan.FromMinutes(15)), Is.Zero);
        Assert.That(service.GetState(key).Tasks, Has.Count.EqualTo(1));

        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.That(service.RemoveInactiveRooms(clock.GetUtcNow() - TimeSpan.FromMinutes(15)), Is.EqualTo(1));
        Assert.That(service.GetState(key).Tasks, Is.Empty);
    }

    [Test]
    public void RemoveInactiveRooms_KeepsRoomWithOnlineParticipant()
    {
        var clock = new ManualTimeProvider();
        var service = new PlanningRoomService(clock);
        var key = new RoomKey(Guid.NewGuid(), Guid.NewGuid());
        service.EnsureSeeded(key, [Task(Guid.NewGuid(), "Task", 0)], Scale);
        service.Join(key, "alice", "Alice", "conn-a");
        clock.Advance(TimeSpan.FromHours(1));

        var removed = service.RemoveInactiveRooms(clock.GetUtcNow() - TimeSpan.FromMinutes(15));

        Assert.That(removed, Is.Zero);
        Assert.That(service.GetState(key).Participants, Has.Count.EqualTo(1));
    }

    [Test]
    public void ConcurrentCasts_FromDistinctParticipants_AllLand()
    {
        const int participantCount = 50;
        var key = new RoomKey(Guid.NewGuid(), Guid.NewGuid());
        var scale = Enumerable.Range(0, participantCount).Select(i => i.ToString()).ToArray();
        _service.EnsureSeeded(key, [Task(Guid.NewGuid(), "Task", 0)], scale);

        for (var i = 0; i < participantCount; i++)
        {
            _service.Join(key, $"p{i}", $"P{i:D2}", $"conn-{i}");
        }

        Parallel.For(0, participantCount, i => _service.CastVote(key, $"p{i}", i.ToString()));

        var state = _service.RevealVotes(key);

        Assert.That(state.Participants, Has.Count.EqualTo(participantCount));
        Assert.That(state.Participants.All(p => p.Vote is not null), Is.True);
        var votes = state.Participants.Select(p => p.Vote).ToHashSet();
        Assert.That(votes, Has.Count.EqualTo(participantCount));
    }

    [Test]
    public void ConcurrentMutations_AssignUniqueIncreasingRevisions()
    {
        const int participantCount = 50;
        var key = new RoomKey(Guid.NewGuid(), Guid.NewGuid());
        var scale = Enumerable.Range(0, participantCount).Select(i => i.ToString()).ToArray();
        _service.EnsureSeeded(key, [Task(Guid.NewGuid(), "Task", 0)], scale);

        for (var i = 0; i < participantCount; i++)
        {
            _service.Join(key, $"p{i}", $"P{i:D2}", $"conn-{i}");
        }

        var baselineRevision = _service.GetState(key).Revision;
        var returnedRevisions = new ConcurrentBag<long>();

        Parallel.For(0, participantCount, i =>
        {
            var state = _service.CastVote(key, $"p{i}", i.ToString());
            returnedRevisions.Add(state.Revision);
        });

        Assert.That(
            returnedRevisions.Order(),
            Is.EqualTo(Enumerable.Range(1, participantCount).Select(i => baselineRevision + i)));
        Assert.That(_service.GetState(key).Revision, Is.EqualTo(baselineRevision + participantCount));
    }

    [Test]
    public void EnsureSeeded_SetsFirstTaskBySortOrderActive_AndExposesTasksAndScale()
    {
        var key = new RoomKey(Guid.NewGuid(), Guid.NewGuid());
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        var state = _service.EnsureSeeded(
            key,
            [Task(second, "Second", 1), Task(first, "First", 0)],
            ["1", "2", "3"]);

        Assert.That(state.CurrentTaskId, Is.EqualTo(first));
        Assert.That(state.Tasks.Select(t => t.TaskId), Is.EqualTo(new[] { first, second }));
        Assert.That(state.ScaleValues, Is.EqualTo(new[] { "1", "2", "3" }));
    }

    [Test]
    public void EnsureSeeded_IsIdempotent_DoesNotResetActiveTaskOrVotes()
    {
        var key = new RoomKey(Guid.NewGuid(), Guid.NewGuid());
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        _service.EnsureSeeded(key, [Task(first, "Task 1", 0), Task(second, "Task 2", 1)], Scale);
        _service.SetActiveTask(key, second);
        _service.Join(key, "alice", "Alice", "conn-a");
        _service.CastVote(key, "alice", "5");

        var state = _service.EnsureSeeded(key, [Task(Guid.NewGuid(), "Ignored", 0)], ["99"]);

        Assert.That(state.CurrentTaskId, Is.EqualTo(second));
        Assert.That(Participant(state, "alice").HasVoted, Is.True);
        Assert.That(state.ScaleValues, Does.Not.Contain("99"));
    }

    [Test]
    public void CastVote_WithValueOutsideScale_IsRejected()
    {
        _service.Join(_key, "alice", "Alice", "conn-a");

        Assert.Throws<InvalidOperationException>(() => _service.CastVote(_key, "alice", "999"));
    }

    [Test]
    public void RevealAndReset_AreScopedToActiveTask_OtherTasksUnaffected()
    {
        var key = new RoomKey(Guid.NewGuid(), Guid.NewGuid());
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        _service.EnsureSeeded(key, [Task(first, "Task 1", 0), Task(second, "Task 2", 1)], Scale);
        _service.Join(key, "alice", "Alice", "conn-a");

        _service.CastVote(key, "alice", "5");
        _service.RevealVotes(key);

        _service.SetActiveTask(key, second);
        _service.CastVote(key, "alice", "8");

        var secondState = _service.GetState(key);
        Assert.That(secondState.IsRevealed, Is.False);
        Assert.That(Participant(secondState, "alice").Vote, Is.Null);
        Assert.That(Participant(secondState, "alice").HasVoted, Is.True);

        var firstState = _service.SetActiveTask(key, first);
        Assert.That(firstState.IsRevealed, Is.True);
        Assert.That(Participant(firstState, "alice").Vote, Is.EqualTo("5"));

        _service.SetActiveTask(key, second);
        _service.ResetRound(key, second);

        var firstAfterReset = _service.SetActiveTask(key, first);
        Assert.That(firstAfterReset.IsRevealed, Is.True);
        Assert.That(Participant(firstAfterReset, "alice").Vote, Is.EqualTo("5"));
    }

    [Test]
    public void SetActiveTask_SwitchesScope_AndCarriesIndependentVotes()
    {
        var key = new RoomKey(Guid.NewGuid(), Guid.NewGuid());
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        _service.EnsureSeeded(key, [Task(first, "Task 1", 0), Task(second, "Task 2", 1)], Scale);
        _service.Join(key, "alice", "Alice", "conn-a");

        _service.CastVote(key, "alice", "5");
        _service.SetActiveTask(key, second);
        _service.CastVote(key, "alice", "13");

        _service.SetActiveTask(key, first);
        var firstRevealed = _service.RevealVotes(key);
        Assert.That(Participant(firstRevealed, "alice").Vote, Is.EqualTo("5"));

        _service.SetActiveTask(key, second);
        var secondRevealed = _service.RevealVotes(key);
        Assert.That(Participant(secondRevealed, "alice").Vote, Is.EqualTo("13"));
    }

    [Test]
    public void SetActiveTask_WithUnknownTask_IsRejected()
    {
        Assert.Throws<InvalidOperationException>(() => _service.SetActiveTask(_key, Guid.NewGuid()));
    }

    [Test]
    public void ApplyAgreedEstimate_SurfacesOnTask_AndResetClearsIt()
    {
        _service.Join(_key, "alice", "Alice", "conn-a");
        _service.CastVote(_key, "alice", "5");
        _service.RevealVotes(_key);

        var picked = _service.ApplyAgreedEstimate(_key, _taskId, "5");
        Assert.That(picked.Tasks.Single(t => t.TaskId == _taskId).AgreedEstimate, Is.EqualTo("5"));

        var afterReset = _service.ResetRound(_key, _taskId);
        Assert.That(afterReset.Tasks.Single(t => t.TaskId == _taskId).AgreedEstimate, Is.Null);
        Assert.That(afterReset.IsRevealed, Is.False);
        Assert.That(Participant(afterReset, "alice").HasVoted, Is.False);
    }

    [Test]
    public void ResetRound_TargetsExpectedTask_WhenActiveTaskChanges()
    {
        var key = new RoomKey(Guid.NewGuid(), Guid.NewGuid());
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        _service.EnsureSeeded(key, [Task(first, "Task 1", 0), Task(second, "Task 2", 1)], Scale);
        _service.Join(key, "alice", "Alice", "conn-a");
        _service.CastVote(key, "alice", "5");
        _service.RevealVotes(key);
        _service.ApplyAgreedEstimate(key, first, "5");
        _service.SetActiveTask(key, second);

        var state = _service.ResetRound(key, first);

        Assert.That(state.CurrentTaskId, Is.EqualTo(second));
        Assert.That(state.Tasks.Single(task => task.TaskId == first).AgreedEstimate, Is.Null);

        var firstState = _service.SetActiveTask(key, first);
        Assert.That(firstState.IsRevealed, Is.False);
        Assert.That(Participant(firstState, "alice").HasVoted, Is.False);
    }

    [Test]
    public void SyncTasks_AddsNewTask_KeepingExistingActiveTaskAndVotes()
    {
        _service.Join(_key, "alice", "Alice", "conn-a");
        _service.CastVote(_key, "alice", "5");
        var added = Guid.NewGuid();

        var state = _service.SyncTasks(_key,
        [
            Task(_taskId, "Task 1", 0),
            Task(added, "Task 2", 1)
        ]);

        Assert.That(state.Tasks.Select(t => t.TaskId), Is.EqualTo(new[] { _taskId, added }));
        Assert.That(state.CurrentTaskId, Is.EqualTo(_taskId));
        Assert.That(Participant(state, "alice").HasVoted, Is.True);
    }

    [Test]
    public void SyncTasks_UpdatesTitleAndDescription_PreservingVotesAndAgreedEstimate()
    {
        _service.Join(_key, "alice", "Alice", "conn-a");
        _service.CastVote(_key, "alice", "5");
        _service.RevealVotes(_key);
        _service.ApplyAgreedEstimate(_key, _taskId, "5");

        var state = _service.SyncTasks(_key,
        [
            Task(_taskId, "Renamed", 0, agreedEstimate: "ignored-on-update", description: "## Spec")
        ]);

        var task = state.Tasks.Single(t => t.TaskId == _taskId);
        Assert.That(task.Title, Is.EqualTo("Renamed"));
        Assert.That(task.Description, Is.EqualTo("## Spec"));
        Assert.That(task.AgreedEstimate, Is.EqualTo("5"));
        Assert.That(state.IsRevealed, Is.True);
        Assert.That(Participant(state, "alice").Vote, Is.EqualTo("5"));
    }

    [Test]
    public void SyncTasks_RemovesMissingTask_AndKeepsActiveWhenSurvivorRemains()
    {
        var second = Guid.NewGuid();
        _service.SyncTasks(_key, [Task(_taskId, "Task 1", 0), Task(second, "Task 2", 1)]);
        _service.SetActiveTask(_key, second);

        var state = _service.SyncTasks(_key, [Task(second, "Task 2", 1)]);

        Assert.That(state.Tasks.Select(t => t.TaskId), Is.EqualTo(new[] { second }));
        Assert.That(state.CurrentTaskId, Is.EqualTo(second));
    }

    [Test]
    public void SyncTasks_ResetsActiveTask_OnlyWhenActiveTaskRemoved()
    {
        var second = Guid.NewGuid();
        _service.SyncTasks(_key, [Task(_taskId, "Task 1", 0), Task(second, "Task 2", 1)]);
        Assert.That(_service.GetState(_key).CurrentTaskId, Is.EqualTo(_taskId));

        var state = _service.SyncTasks(_key, [Task(second, "Task 2", 0)]);

        Assert.That(state.CurrentTaskId, Is.EqualTo(second));
    }

    [Test]
    public void SyncTasks_OnUnseededRoom_DoesNotPopulateTasks()
    {
        var key = new RoomKey(Guid.NewGuid(), Guid.NewGuid());

        var state = _service.SyncTasks(key, [Task(Guid.NewGuid(), "Phantom", 0)]);

        Assert.That(state.Tasks, Is.Empty);
        Assert.That(state.CurrentTaskId, Is.Null);
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration) => _utcNow += duration;
    }
}
