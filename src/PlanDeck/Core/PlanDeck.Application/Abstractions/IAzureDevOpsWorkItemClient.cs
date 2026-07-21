namespace PlanDeck.Application.Abstractions;

/// <summary>
/// Per-request connection context that replaces global ADO configuration.
/// Resolved server-side from the project's stored connection and Key Vault secret.
/// </summary>
public sealed record AdoConnectionContext(
    string OrganizationUrl,
    string Project,
    string PersonalAccessToken,
    string EstimateField,
    string DescriptionField,
    string ReproStepsField,
    string AcceptanceCriteriaField);

public interface IAzureDevOpsWorkItemClient
{
    Task<IReadOnlyCollection<AzureDevOpsWorkItem>> ImportWorkItemsAsync(
        AdoConnectionContext connection,
        AzureDevOpsImportRequest request,
        CancellationToken cancellationToken);

    Task<AzureDevOpsWorkItem?> GetWorkItemByIdAsync(
        AdoConnectionContext connection,
        int workItemId,
        CancellationToken cancellationToken);

    Task<AzureDevOpsWriteEstimateResult> WriteEstimateAsync(
        AdoConnectionContext connection,
        AzureDevOpsWriteEstimateRequest request,
        CancellationToken cancellationToken);
}

public sealed record AzureDevOpsImportRequest(string? WiqlWhereClause, int Limit);

public sealed record AzureDevOpsWorkItem(
    int Id,
    string Title,
    string State,
    string WorkItemType,
    int Revision,
    double? Estimate,
    string? Description = null);

public sealed record AzureDevOpsWriteEstimateRequest(int WorkItemId, int? ExpectedRevision, double Estimate);

public sealed record AzureDevOpsWriteEstimateResult(int WorkItemId, int Revision);

public sealed class AzureDevOpsConcurrencyException(string message) : Exception(message);

public sealed class AzureDevOpsRateLimitException(string message) : Exception(message);
