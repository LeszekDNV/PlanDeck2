namespace PlanDeck.Application.Account;

public sealed record LocalRegisterResult(
    LocalRegisterStatus Status,
    Guid? UserId = null,
    IReadOnlyList<string>? Errors = null)
{
    public bool Succeeded => Status == LocalRegisterStatus.Success;

    public static LocalRegisterResult Success(Guid userId) =>
        new(LocalRegisterStatus.Success, userId, []);

    public static LocalRegisterResult Failure(LocalRegisterStatus status, params string[] errors) =>
        new(status, null, errors.ToList());
}
