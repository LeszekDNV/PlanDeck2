namespace PlanDeck.Application.Account;

public enum LocalRegisterStatus
{
    Success,
    InvalidUserName,
    InvalidEmail,
    InvalidPassword,
    WeakPassword,
    DuplicateUserName,
    DuplicateEmail,
    PublicRegistrationDisabled,
    InvitationInvalidOrExpired,
    Failure
}
