namespace PlanDeck.Application.Abstractions;

public sealed record IdentityAccount(
    Guid Id,
    string NormalizedUserName,
    string? NormalizedEmail,
    bool EmailConfirmed);

