namespace PlanDeck.Core.Shared.Realtime;

public sealed record PlanningRoomState(
    string SessionId,
    Guid? CurrentTaskId,
    bool IsRevealed,
    IReadOnlyCollection<PlanningParticipantState> Participants,
    IReadOnlyList<PlanningTaskState> Tasks,
    IReadOnlyList<string> ScaleValues,
    long Revision);

public sealed record PlanningTaskState(
    Guid TaskId,
    string Title,
    string? Description,
    int SortOrder,
    string? AgreedEstimate);

public sealed record PlanningParticipantState(
    string ParticipantId,
    string DisplayName,
    bool HasVoted,
    string? Vote,
    bool IsOnline);
