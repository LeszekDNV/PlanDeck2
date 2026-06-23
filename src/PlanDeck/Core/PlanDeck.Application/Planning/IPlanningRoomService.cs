using PlanDeck.Core.Shared.Realtime;

namespace PlanDeck.Application.Planning;

public interface IPlanningRoomService
{
    PlanningRoomState EnsureSeeded(
        RoomKey key,
        IReadOnlyList<(Guid TaskId, string Title, int SortOrder, string? AgreedEstimate)> tasks,
        IReadOnlyList<string> scaleValues);

    PlanningRoomState Join(RoomKey key, string participantId, string displayName, string connectionId);

    PlanningRoomState Leave(RoomKey key, string participantId, string connectionId);

    (RoomKey Key, PlanningRoomState State)? Disconnect(string connectionId);

    PlanningRoomState CastVote(RoomKey key, string participantId, string vote);

    PlanningRoomState RevealVotes(RoomKey key);

    PlanningRoomState ResetRound(RoomKey key);

    PlanningRoomState SetActiveTask(RoomKey key, Guid taskId);

    PlanningRoomState ApplyAgreedEstimate(RoomKey key, Guid taskId, string? estimate);

    bool IsValidEstimate(RoomKey key, string? estimate);

    PlanningRoomState GetState(RoomKey key);
}
