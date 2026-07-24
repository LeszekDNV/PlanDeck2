namespace PlanDeck.Application.Abstractions;

public interface IIdentityAccountRepository
{
    Task<IdentityAccount?> FindByNormalizedEmailAsync(
        string normalizedEmail,
        CancellationToken cancellationToken = default);

    Task<IdentityAccount?> FindByNormalizedUserNameAsync(
        string normalizedUserName,
        CancellationToken cancellationToken = default);

    Task<IdentityAccount?> FindByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);
}

