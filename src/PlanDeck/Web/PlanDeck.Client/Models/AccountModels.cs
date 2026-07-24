namespace PlanDeck.Client.Models;

public sealed record AccountActionResponse(
    string Status,
    Guid? UserId = null,
    IReadOnlyList<string>? Errors = null,
    string? ReturnUrl = null)
{
    public bool Succeeded => Status == "Success";
}

public sealed record LocalRegisterModel(
    string Email,
    string FirstName,
    string LastName,
    string UserName,
    string Password,
    string? InvitationToken = null);

public sealed record LocalLoginModel(
    string Login,
    string Password,
    bool RememberMe = false);

public sealed record ForgotPasswordModel(string Email);

public sealed record ResetPasswordModel(
    string Email,
    string Token,
    string NewPassword);

public sealed record ResendConfirmationModel(string Email);

public sealed record LinkEntraModel(
    string Password,
    string? ReturnUrl = null);

public sealed record UnlinkEntraModel(
    string Provider,
    string ProviderKey);

public sealed record UserLoginInfoModel(
    string Provider,
    string ProviderKey);

public sealed record SecurityInfoModel(
    Guid UserId,
    string UserName,
    string? Email,
    bool EmailConfirmed,
    IReadOnlyList<LinkedLoginInfoModel> Logins);

public sealed record LinkedLoginInfoModel(
    string Provider,
    string? DisplayName);
