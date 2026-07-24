namespace PlanDeck.Application.Account;

public enum EntraCallbackStatus
{
    Success,
    ExternalIdentityNotFound,
    AccountInactive,
    EmailRequired,
    DuplicateUserName,
    DuplicateEmail,
    AccountTenantConflict,
    PublicRegistrationDisabled,
    AlreadyLinked,
    ExternalIdentityUsedElsewhere,
    AccountNotFound,
    InvalidState
}
