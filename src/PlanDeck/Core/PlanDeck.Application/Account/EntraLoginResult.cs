namespace PlanDeck.Application.Account;

public sealed record EntraLoginResult(
    EntraCallbackStatus Status,
    Guid? UserId = null,
    IReadOnlyList<string>? Errors = null)
{
    public bool Succeeded => Status == EntraCallbackStatus.Success;

    public static EntraLoginResult Success(Guid userId) =>
        new(EntraCallbackStatus.Success, userId, []);

    public static EntraLoginResult Failure(EntraCallbackStatus status, params string[] errors) =>
        new(status, null, errors.ToList());
}
