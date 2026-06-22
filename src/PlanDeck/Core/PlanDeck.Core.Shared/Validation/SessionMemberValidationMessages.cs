namespace PlanDeck.Core.Shared.Validation;

/// <summary>
/// Stable validation detail strings shared between the server (thrown as gRPC
/// <c>InvalidArgument</c> status details) and the client (mapped to localized messages).
/// Keeping them in one place avoids brittle string matching across the wire.
/// </summary>
public static class SessionMemberValidationMessages
{
    public const string SessionIdRequired = "SessionId is required.";

    public const string EmailRequired = "A valid member email is required.";
}
