using PlanDeck.Application.Account;

namespace PlanDeck.Application.Abstractions;

public interface IAccountLifecycleService
{
    Task<ConfirmEmailResult> ConfirmEmailAsync(
        Guid userId,
        string token,
        CancellationToken cancellationToken = default);

    Task<ResendConfirmationResult> ResendConfirmationAsync(
        string email,
        CancellationToken cancellationToken = default);

    Task<ForgotPasswordResult> SendPasswordResetAsync(
        string email,
        CancellationToken cancellationToken = default);

    Task<ResetPasswordResult> ResetPasswordAsync(
        string email,
        string token,
        string newPassword,
        CancellationToken cancellationToken = default);
}
