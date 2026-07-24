namespace PlanDeck.Application.Account;

public enum ResetPasswordStatus
{
    Success,
    InvalidToken,
    WeakPassword,
    Failure
}

public sealed record ResetPasswordResult(ResetPasswordStatus Status, IReadOnlyList<string>? Errors = null)
{
    public static ResetPasswordResult Success() => new(ResetPasswordStatus.Success);
    public static ResetPasswordResult InvalidToken(IReadOnlyList<string>? errors = null) => new(ResetPasswordStatus.InvalidToken, errors);
    public static ResetPasswordResult WeakPassword(IReadOnlyList<string>? errors = null) => new(ResetPasswordStatus.WeakPassword, errors);
    public static ResetPasswordResult Failure(IReadOnlyList<string>? errors = null) => new(ResetPasswordStatus.Failure, errors);
}
