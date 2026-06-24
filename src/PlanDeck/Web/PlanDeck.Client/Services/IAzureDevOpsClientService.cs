using PlanDeck.Core.Shared.Contracts;

namespace PlanDeck.Client.Services;

public interface IAzureDevOpsClientService
{
    Task<IReadOnlyCollection<AzureDevOpsWorkItemDto>> ImportWorkItemsAsync(
        IReadOnlyCollection<string> workItemTypes, IReadOnlyCollection<string> states, int limit = 100);

    Task<WriteEstimateReply> WriteEstimateAsync(int workItemId, int? expectedRevision, double estimate);
}
