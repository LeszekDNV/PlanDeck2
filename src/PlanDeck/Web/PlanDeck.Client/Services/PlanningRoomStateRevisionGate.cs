using PlanDeck.Core.Shared.Realtime;

namespace PlanDeck.Client.Services;

public sealed class PlanningRoomStateRevisionGate
{
    private readonly Lock _sync = new();
    private long _greatestAppliedRevision = -1;

    public bool TryAccept(PlanningRoomState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        lock (_sync)
        {
            if (state.Revision < _greatestAppliedRevision)
            {
                return false;
            }

            _greatestAppliedRevision = state.Revision;
            return true;
        }
    }

    public void Reset()
    {
        lock (_sync)
        {
            _greatestAppliedRevision = -1;
        }
    }
}
