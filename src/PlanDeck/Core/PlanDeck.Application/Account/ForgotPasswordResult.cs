namespace PlanDeck.Application.Account;

public enum ForgotPasswordStatus
{
    Sent,
    SendFailed,
    Failure
}

public sealed record ForgotPasswordResult(ForgotPasswordStatus Status, IReadOnlyList<string>? Errors = null)
{
    public static ForgotPasswordResult Sent() => new(ForgotPasswordStatus.Sent);
    public static ForgotPasswordResult SendFailed(IReadOnlyList<string>? errors = null) => new(ForgotPasswordStatus.SendFailed, errors);
    public static ForgotPasswordResult Failure(IReadOnlyList<string>? errors = null) => new(ForgotPasswordStatus.Failure, errors);
}
