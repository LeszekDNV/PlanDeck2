using PlanDeck.Core.Shared.Realtime;

namespace PlanDeck.Application.Planning;

public interface IPlanningRoomService
{
    PlanningRoomState Join(string sessionId, string participantId, string displayName);

    PlanningRoomState Leave(string sessionId, string participantId);

    PlanningRoomState CastVote(string sessionId, string participantId, string vote);

    PlanningRoomState RevealVotes(string sessionId);

    PlanningRoomState ResetRound(string sessionId);
}
