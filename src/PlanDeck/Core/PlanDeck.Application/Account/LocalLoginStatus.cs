namespace PlanDeck.Application.Account;

public enum LocalLoginStatus
{
    Success,
    InvalidCredentials,
    EmailNotConfirmed,
    LockedOut,
    Failure
}
