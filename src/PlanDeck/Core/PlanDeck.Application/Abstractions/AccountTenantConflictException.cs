namespace PlanDeck.Application.Abstractions;

public sealed class AccountTenantConflictException(string email)
    : Exception($"The account for email '{email}' belongs to a different tenant.")
{
    public string Email { get; } = email;
}
