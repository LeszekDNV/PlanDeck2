namespace PlanDeck.Core.Shared.Validation;

/// <summary>
/// Stable validation detail strings shared between the server (thrown as gRPC
/// <c>InvalidArgument</c> status details) and the client (mapped to localized messages).
/// Keeping them in one place avoids brittle string matching across the wire.
/// </summary>
public static class SessionValidationMessages
{
    public const string NameRequired = "Session name is required.";

    public const string CustomScaleRequired = "A custom scale requires at least one value.";

    public const string UnknownScaleType = "Unknown voting scale type.";

    public const string TaskTitleRequired = "A task title is required.";
}
