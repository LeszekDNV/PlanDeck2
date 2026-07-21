using PlanDeck.Core.Shared.Contracts;

namespace PlanDeck.Client.Services;

public interface IAzureDevOpsClientService
{
    Task<IReadOnlyCollection<AzureDevOpsWorkItemDto>> ImportWorkItemsAsync(
        Guid projectId,
        IReadOnlyCollection<string> workItemTypes,
        IReadOnlyCollection<string> states,
        int limit = 100);
}
