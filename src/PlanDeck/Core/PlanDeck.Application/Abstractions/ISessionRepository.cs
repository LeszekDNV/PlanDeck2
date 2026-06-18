using PlanDeck.Application.Domain;

namespace PlanDeck.Application.Abstractions;

public interface ISessionRepository
{
    Task<PlanningSession> CreateSessionAsync(PlanningSession session, CancellationToken cancellationToken);

    Task<IReadOnlyList<PlanningSession>> GetSessionsAsync(CancellationToken cancellationToken);

    Task<PlanningSession?> GetSessionAsync(Guid id, CancellationToken cancellationToken);

    Task<PlanningSession> UpdateSessionAsync(PlanningSession session, CancellationToken cancellationToken);

    Task<bool> DeleteSessionAsync(Guid id, CancellationToken cancellationToken);
}

public sealed class SessionNotFoundException(Guid sessionId)
    : Exception($"Session '{sessionId}' was not found in the current tenant.")
{
    public Guid SessionId { get; } = sessionId;
}

public sealed class SessionNotDraftException(Guid sessionId)
    : Exception($"Session '{sessionId}' is not in Draft status and cannot be modified.")
{
    public Guid SessionId { get; } = sessionId;
}
