using PlanDeck.Application.Domain;

namespace PlanDeck.Application.Abstractions;

public interface ISessionAccessResolver
{
    Task<(Guid ProjectId, ProjectRole Role)?> ResolveProjectAccessAsync(
        Guid sessionId,
        CancellationToken cancellationToken);
}
