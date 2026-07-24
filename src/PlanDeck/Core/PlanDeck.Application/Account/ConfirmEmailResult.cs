namespace PlanDeck.Application.Account;

public enum ConfirmEmailStatus
{
    Success,
    AlreadyConfirmed,
    InvalidToken,
    Failure
}

public sealed record ConfirmEmailResult(ConfirmEmailStatus Status, IReadOnlyList<string>? Errors = null)
{
    public static ConfirmEmailResult Success() => new(ConfirmEmailStatus.Success);
    public static ConfirmEmailResult AlreadyConfirmed() => new(ConfirmEmailStatus.AlreadyConfirmed);
    public static ConfirmEmailResult InvalidToken(IReadOnlyList<string>? errors = null) => new(ConfirmEmailStatus.InvalidToken, errors);
    public static ConfirmEmailResult Failure(IReadOnlyList<string>? errors = null) => new(ConfirmEmailStatus.Failure, errors);
}
