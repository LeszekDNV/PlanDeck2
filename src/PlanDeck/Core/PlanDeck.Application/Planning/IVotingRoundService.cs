using PlanDeck.Application.Abstractions;

namespace PlanDeck.Application.Planning;

public interface IVotingRoundService
{
    Task<bool> IsAuthorizedParticipantAsync(Guid sessionId, Guid userId, string? email, CancellationToken cancellationToken);

    Task<RoomSeed?> AuthorizeAndLoadSeedAsync(Guid sessionId, Guid userId, string? email, CancellationToken cancellationToken);

    Task<bool> SelectEstimateAsync(Guid sessionId, Guid taskId, string? estimate, CancellationToken cancellationToken);
}

public sealed record RoomSeed(
    IReadOnlyList<PlanningRoomTaskSnapshot> Tasks,
    IReadOnlyList<string> ScaleValues);
