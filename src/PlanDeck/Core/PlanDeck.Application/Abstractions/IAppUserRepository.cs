using PlanDeck.Application.Domain;

namespace PlanDeck.Application.Abstractions;

public interface IAppUserRepository
{
    Task<AppUser?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<bool> IsActiveAsync(Guid tenantId, Guid id, CancellationToken cancellationToken = default);
}
