namespace PlanDeck.Application.Account;

public sealed record EntraRegisterResult(
    EntraCallbackStatus Status,
    Guid? UserId = null,
    IReadOnlyList<string>? Errors = null)
{
    public bool Succeeded => Status == EntraCallbackStatus.Success;

    public static EntraRegisterResult Success(Guid userId) =>
        new(EntraCallbackStatus.Success, userId, []);

    public static EntraRegisterResult Failure(EntraCallbackStatus status, params string[] errors) =>
        new(status, null, errors.ToList());
}
