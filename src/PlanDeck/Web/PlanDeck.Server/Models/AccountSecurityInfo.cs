namespace PlanDeck.Server.Models;

public sealed record AccountSecurityInfo(
    Guid UserId,
    string UserName,
    string? Email,
    bool EmailConfirmed,
    IReadOnlyList<LinkedLoginInfo> Logins);

public sealed record LinkedLoginInfo(
    string Provider,
    string? DisplayName);
