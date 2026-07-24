namespace PlanDeck.Application.Account;

public sealed record LocalLoginRequest(
    string Login,
    string Password,
    bool RememberMe = false);
