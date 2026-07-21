using PlanDeck.Client.Services;
using PlanDeck.Core.Shared.Realtime;

namespace PlanDeck.Unit.Tests.Realtime;

[TestFixture]
public sealed class PlanningRoomStateOrderingTests
{
    [Test]
    public void RevisionGate_ReceivesTwoOneThree_ExposesOnlyTwoAndThree()
    {
        var gate = new PlanningRoomStateRevisionGate();
        var observed = new List<long>();

        foreach (var revision in new long[] { 2, 1, 3 })
        {
            var state = State(revision);
            if (gate.TryAccept(state))
            {
                observed.Add(state.Revision);
            }
        }

        Assert.That(observed, Is.EqualTo(new long[] { 2, 3 }));
    }

    [Test]
    public void Reset_AllowsRevisionToRestartFromZero()
    {
        var gate = new PlanningRoomStateRevisionGate();
        Assert.That(gate.TryAccept(State(5)), Is.True);

        gate.Reset();

        Assert.That(gate.TryAccept(State(0)), Is.True);
    }

    private static PlanningRoomState State(long revision) =>
        new(
            Guid.NewGuid().ToString(),
            null,
            false,
            [],
            [],
            [],
            revision);
}
