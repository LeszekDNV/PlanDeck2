namespace PlanDeck.Application.Account;

public sealed record EntraLinkResult(
    EntraCallbackStatus Status,
    IReadOnlyList<string>? Errors = null)
{
    public bool Succeeded => Status == EntraCallbackStatus.Success;

    public static EntraLinkResult Success() =>
        new(EntraCallbackStatus.Success, []);

    public static EntraLinkResult Failure(EntraCallbackStatus status, params string[] errors) =>
        new(status, errors.ToList());
}
