namespace PlanDeck.Application.Abstractions;

public interface IAzureDevOpsWorkItemClient
{
    Task<IReadOnlyCollection<AzureDevOpsWorkItem>> ImportWorkItemsAsync(AzureDevOpsImportRequest request, CancellationToken cancellationToken);

    Task<AzureDevOpsWriteEstimateResult> WriteEstimateAsync(AzureDevOpsWriteEstimateRequest request, CancellationToken cancellationToken);
}

public sealed record AzureDevOpsImportRequest(string? WiqlWhereClause, int Limit);

public sealed record AzureDevOpsWorkItem(
    int Id,
    string Title,
    string State,
    string WorkItemType,
    int Revision,
    double? Estimate);

public sealed record AzureDevOpsWriteEstimateRequest(int WorkItemId, int? ExpectedRevision, double Estimate);

public sealed record AzureDevOpsWriteEstimateResult(int WorkItemId, int Revision);
