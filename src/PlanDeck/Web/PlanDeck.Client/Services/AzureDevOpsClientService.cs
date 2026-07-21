using Grpc.Net.Client;
using PlanDeck.Core.Shared.Contracts;
using ProtoBuf.Grpc.Client;

namespace PlanDeck.Client.Services;

public sealed class AzureDevOpsClientService(GrpcChannel channel) : IAzureDevOpsClientService
{
    public async Task<IReadOnlyCollection<AzureDevOpsWorkItemDto>> ImportWorkItemsAsync(
        Guid projectId,
        IReadOnlyCollection<string> workItemTypes,
        IReadOnlyCollection<string> states,
        int limit = 100)
    {
        var service = channel.CreateGrpcService<IAzureDevOpsWorkItemService>();
        var reply = await service.ImportWorkItemsAsync(new ImportWorkItemsRequest
        {
            ProjectId = projectId,
            WorkItemTypes = workItemTypes.ToList(),
            States = states.ToList(),
            Limit = limit
        });

        return reply.WorkItems;
    }
}
