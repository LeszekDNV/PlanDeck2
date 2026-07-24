using PlanDeck.Application.Account;

namespace PlanDeck.Application.Abstractions;

public interface ILocalAccountService
{
    Task<LocalRegisterResult> RegisterAsync(
        LocalRegisterRequest request,
        CancellationToken cancellationToken = default);
}
