namespace PlanDeck.Application.Account;

public sealed record LocalLoginResult(
    LocalLoginStatus Status,
    Guid? UserId = null,
    IReadOnlyList<string>? Errors = null)
{
    public bool Succeeded => Status == LocalLoginStatus.Success;

    public static LocalLoginResult Success(Guid userId) =>
        new(LocalLoginStatus.Success, userId, []);

    public static LocalLoginResult Failure(LocalLoginStatus status, params string[] errors) =>
        new(status, null, errors.ToList());
}
