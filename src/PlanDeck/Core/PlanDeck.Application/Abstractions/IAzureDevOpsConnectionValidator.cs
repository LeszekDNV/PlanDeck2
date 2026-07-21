namespace PlanDeck.Application.Abstractions;

public interface IAzureDevOpsConnectionValidator
{
    Task ValidateAsync(
        AzureDevOpsConnectionValidationRequest request,
        CancellationToken cancellationToken);
}

public sealed record AzureDevOpsConnectionValidationRequest(
    string OrganizationUrl,
    string Project,
    string PersonalAccessToken);

public enum AzureDevOpsConnectionValidationFailure
{
    InvalidCredentials,
    Forbidden,
    ProjectNotFound,
    Unavailable
}

public sealed class AzureDevOpsConnectionValidationException(
    AzureDevOpsConnectionValidationFailure failure)
    : Exception("The Azure DevOps connection could not be validated.")
{
    public AzureDevOpsConnectionValidationFailure Failure { get; } = failure;
}
