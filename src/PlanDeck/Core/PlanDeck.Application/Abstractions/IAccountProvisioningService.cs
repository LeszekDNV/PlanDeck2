namespace PlanDeck.Application.Abstractions;

public interface IAccountProvisioningService
{
    Task<AccountProvisioningResult> ProvisionOwnerAsync(
        string userName,
        string email,
        string firstName,
        string lastName,
        string password,
        CancellationToken cancellationToken = default);
}

