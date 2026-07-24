namespace PlanDeck.Application.Account;

public sealed record LinkEntraRequest(
    string Password,
    string? ReturnUrl = null);
