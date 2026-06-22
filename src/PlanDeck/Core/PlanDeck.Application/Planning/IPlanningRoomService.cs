using PlanDeck.Core.Shared.Realtime;

namespace PlanDeck.Application.Planning;

public interface IPlanningRoomService
{
    PlanningRoomState Join(RoomKey key, string participantId, string displayName, string connectionId);

    PlanningRoomState Leave(RoomKey key, string participantId, string connectionId);

    (RoomKey Key, PlanningRoomState State)? Disconnect(string connectionId);

    PlanningRoomState CastVote(RoomKey key, string participantId, string vote);

    PlanningRoomState RevealVotes(RoomKey key);

    PlanningRoomState ResetRound(RoomKey key);

    PlanningRoomState GetState(RoomKey key);
}
