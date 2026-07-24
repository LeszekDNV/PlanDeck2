namespace PlanDeck.Application.Account;

public sealed record UnlinkEntraRequest(
    string Provider,
    string ProviderKey);
