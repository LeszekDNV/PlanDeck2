namespace PlanDeck.Core.Shared.Realtime;

public sealed record PlanningRoomState(
    string SessionId,
    bool IsRevealed,
    IReadOnlyCollection<PlanningParticipantState> Participants);

public sealed record PlanningParticipantState(
    string ParticipantId,
    string DisplayName,
    bool HasVoted,
    string? Vote);
