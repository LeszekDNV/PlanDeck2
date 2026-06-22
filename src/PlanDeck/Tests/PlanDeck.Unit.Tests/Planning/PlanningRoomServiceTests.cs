using PlanDeck.Application.Planning;
using PlanDeck.Core.Shared.Realtime;

namespace PlanDeck.Unit.Tests.Planning;

[TestFixture]
public sealed class PlanningRoomServiceTests
{
    private PlanningRoomService _service = null!;
    private RoomKey _key;

    [SetUp]
    public void SetUp()
    {
        _service = new PlanningRoomService();
        _key = new RoomKey(Guid.NewGuid(), Guid.NewGuid());
    }

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

        var state = _service.ResetRound(_key);

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
    public void ConcurrentCasts_FromDistinctParticipants_AllLand()
    {
        const int participantCount = 50;
        for (var i = 0; i < participantCount; i++)
        {
            _service.Join(_key, $"p{i}", $"P{i:D2}", $"conn-{i}");
        }

        Parallel.For(0, participantCount, i => _service.CastVote(_key, $"p{i}", i.ToString()));

        var state = _service.RevealVotes(_key);

        Assert.That(state.Participants, Has.Count.EqualTo(participantCount));
        Assert.That(state.Participants.All(p => p.Vote is not null), Is.True);
        var votes = state.Participants.Select(p => p.Vote).ToHashSet();
        Assert.That(votes, Has.Count.EqualTo(participantCount));
    }

    [Test]
    public void ConcurrentJoins_FromSameParticipant_ProduceSingleEntry()
    {
        Parallel.For(0, 32, i => _service.Join(_key, "alice", "Alice", $"conn-{i}"));

        var state = _service.GetState(_key);

        Assert.That(state.Participants, Has.Count.EqualTo(1));
        Assert.That(Participant(state, "alice").IsOnline, Is.True);
    }
}
