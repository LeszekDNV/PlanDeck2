using PlanDeck.Client.Models;

namespace PlanDeck.Client.Services;

public interface IAccountClientService
{
    Task<AccountActionResponse> RegisterAsync(LocalRegisterModel model, CancellationToken cancellationToken = default);

    Task<AccountActionResponse> LoginAsync(LocalLoginModel model, string? returnUrl = null, CancellationToken cancellationToken = default);

    Task<AccountActionResponse> LogoutAsync(CancellationToken cancellationToken = default);

    Task<AccountActionResponse> LogoutGuestAsync(CancellationToken cancellationToken = default);

    Task<AccountActionResponse> ConfirmEmailAsync(Guid userId, string token, CancellationToken cancellationToken = default);

    Task<AccountActionResponse> ResendConfirmationAsync(string email, CancellationToken cancellationToken = default);

    Task<AccountActionResponse> ForgotPasswordAsync(string email, CancellationToken cancellationToken = default);

    Task<AccountActionResponse> ResetPasswordAsync(ResetPasswordModel model, CancellationToken cancellationToken = default);

    Task<bool> IsMicrosoftAuthenticationAvailableAsync(CancellationToken cancellationToken = default);

    void NavigateToEntraLogin(string? returnUrl = null);

    void NavigateToEntraRegister(string? returnUrl = null, string? invitationToken = null);

    Task<AccountActionResponse> LinkEntraAsync(LinkEntraModel model, CancellationToken cancellationToken = default);

    Task<AccountActionResponse> UnlinkEntraAsync(UnlinkEntraModel model, CancellationToken cancellationToken = default);

    Task<SecurityInfoModel> GetSecurityInfoAsync(CancellationToken cancellationToken = default);
}
