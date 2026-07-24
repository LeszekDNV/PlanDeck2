namespace PlanDeck.Application.Account;

public enum ResendConfirmationStatus
{
    Sent,
    SendFailed,
    Failure
}

public sealed record ResendConfirmationResult(ResendConfirmationStatus Status, IReadOnlyList<string>? Errors = null)
{
    public static ResendConfirmationResult Sent() => new(ResendConfirmationStatus.Sent);
    public static ResendConfirmationResult SendFailed(IReadOnlyList<string>? errors = null) => new(ResendConfirmationStatus.SendFailed, errors);
    public static ResendConfirmationResult Failure(IReadOnlyList<string>? errors = null) => new(ResendConfirmationStatus.Failure, errors);
}
