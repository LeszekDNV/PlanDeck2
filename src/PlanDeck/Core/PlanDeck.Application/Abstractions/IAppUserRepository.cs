using PlanDeck.Application.Domain;

namespace PlanDeck.Application.Abstractions;

public interface IAppUserRepository
{
    Task<AppUser> UpsertAsync(
        Guid tenantId,
        Guid entraObjectId,
        string displayName,
        string email,
        CancellationToken cancellationToken);

    Task<bool> IsActiveAsync(Guid tenantId, Guid appUserId, CancellationToken cancellationToken);
}
