namespace PlanDeck.Application.Planning;

public interface IVotingRoundService
{
    Task<bool> IsAssignedMemberAsync(Guid sessionId, string email, CancellationToken cancellationToken);

    Task<RoomSeed?> LoadRoomSeedAsync(Guid sessionId, CancellationToken cancellationToken);

    Task<bool> SelectEstimateAsync(Guid sessionId, Guid taskId, string? estimate, CancellationToken cancellationToken);
}

public sealed record RoomSeed(
    IReadOnlyList<(Guid TaskId, string Title, int SortOrder, string? AgreedEstimate)> Tasks,
    IReadOnlyList<string> ScaleValues);
