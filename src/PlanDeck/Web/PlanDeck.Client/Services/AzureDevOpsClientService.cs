using Grpc.Net.Client;
using PlanDeck.Core.Shared.Contracts;
using ProtoBuf.Grpc.Client;

namespace PlanDeck.Client.Services;

public sealed class AzureDevOpsClientService(GrpcChannel channel) : IAzureDevOpsClientService
{
    public async Task<IReadOnlyCollection<AzureDevOpsWorkItemDto>> ImportWorkItemsAsync(string? wiqlWhereClause = null, int limit = 100)
    {
        var service = channel.CreateGrpcService<IAzureDevOpsWorkItemService>();
        var reply = await service.ImportWorkItemsAsync(new ImportWorkItemsRequest
        {
            WiqlWhereClause = wiqlWhereClause,
            Limit = limit
        });

        return reply.WorkItems;
    }

    public async Task<WriteEstimateReply> WriteEstimateAsync(int workItemId, int? expectedRevision, double estimate)
    {
        var service = channel.CreateGrpcService<IAzureDevOpsWorkItemService>();
        return await service.WriteEstimateAsync(new WriteEstimateRequest
        {
            WorkItemId = workItemId,
            ExpectedRevision = expectedRevision,
            Estimate = estimate
        });
    }
}
