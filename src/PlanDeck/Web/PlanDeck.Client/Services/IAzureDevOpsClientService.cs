using PlanDeck.Core.Shared.Contracts;

namespace PlanDeck.Client.Services;

public interface IAzureDevOpsClientService
{
    Task<IReadOnlyCollection<AzureDevOpsWorkItemDto>> ImportWorkItemsAsync(string? wiqlWhereClause = null, int limit = 100);

    Task<WriteEstimateReply> WriteEstimateAsync(int workItemId, int? expectedRevision, double estimate);
}
