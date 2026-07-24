namespace PlanDeck.Application.Account;

public sealed record ResetPasswordRequest(string Email, string Token, string NewPassword);
