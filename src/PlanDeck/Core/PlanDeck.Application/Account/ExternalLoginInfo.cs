namespace PlanDeck.Application.Account;

public sealed record ExternalLoginInfo(
    string Provider,
    string ProviderKey,
    string? Email,
    string? FirstName,
    string? LastName);
