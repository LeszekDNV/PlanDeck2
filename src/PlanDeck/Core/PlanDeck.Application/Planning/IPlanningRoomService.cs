using PlanDeck.Application.Abstractions;
using PlanDeck.Core.Shared.Realtime;

namespace PlanDeck.Application.Planning;

public interface IPlanningRoomService
{
    PlanningRoomState EnsureSeeded(
        RoomKey key,
        IReadOnlyList<PlanningRoomTaskSnapshot> tasks,
        IReadOnlyList<string> scaleValues);

    PlanningRoomState SyncTasks(RoomKey key, IReadOnlyList<PlanningRoomTaskSnapshot> tasks);

    PlanningRoomState Join(RoomKey key, string participantId, string displayName, string connectionId);

    PlanningRoomState Leave(RoomKey key, string participantId, string connectionId);

    (RoomKey Key, PlanningRoomState State)? Disconnect(string connectionId);

    PlanningRoomState CastVote(RoomKey key, string participantId, string vote);

    PlanningRoomState RevealVotes(RoomKey key);

    PlanningRoomState ResetRound(RoomKey key, Guid taskId);

    PlanningRoomState SetActiveTask(RoomKey key, Guid taskId);

    PlanningRoomState ApplyAgreedEstimate(RoomKey key, Guid taskId, string? estimate);

    bool IsValidEstimate(RoomKey key, string? estimate);

    PlanningRoomState GetState(RoomKey key);

    int RemoveInactiveRooms(DateTimeOffset inactiveSince);
}
