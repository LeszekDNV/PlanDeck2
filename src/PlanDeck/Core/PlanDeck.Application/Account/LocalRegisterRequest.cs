namespace PlanDeck.Application.Account;

public sealed record LocalRegisterRequest(
    string Email,
    string FirstName,
    string LastName,
    string UserName,
    string Password,
    string? InvitationToken = null);
