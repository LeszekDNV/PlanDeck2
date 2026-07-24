using PlanDeck.Application.Account;

namespace PlanDeck.Application.Abstractions;

public interface IExternalAccountService
{
    Task<EntraLoginResult> LoginAsync(
        ExternalLoginInfo loginInfo,
        CancellationToken cancellationToken = default);

    Task<EntraRegisterResult> RegisterAsync(
        ExternalLoginInfo loginInfo,
        string? invitationToken,
        CancellationToken cancellationToken = default);

    Task<EntraLinkResult> LinkAsync(
        Guid currentUserId,
        ExternalLoginInfo loginInfo,
        CancellationToken cancellationToken = default);

    Task<EntraLinkResult> UnlinkAsync(
        Guid currentUserId,
        string provider,
        string providerKey,
        CancellationToken cancellationToken = default);
}
