using Grpc.Core;
using PlanDeck.Application.Abstractions;
using PlanDeck.Core.Shared.AzureDevOps;
using PlanDeck.Core.Shared.Contracts;
using ProtoBuf.Grpc;

namespace PlanDeck.Application.Services;

public sealed class AzureDevOpsWorkItemGrpcService(IAzureDevOpsWorkItemClient client, ICurrentUserContext currentUser) : IAzureDevOpsWorkItemService
{
    public async Task<ImportWorkItemsReply> ImportWorkItemsAsync(ImportWorkItemsRequest request, CallContext context = default)
    {
        GuestAccessGuard.RejectGuests(currentUser);

        var whereClause = AzureDevOpsWiqlBuilder.BuildWhereClause(request.WorkItemTypes, request.States);

        var workItems = await client.ImportWorkItemsAsync(
            new AzureDevOpsImportRequest(whereClause, request.Limit),
            context.CancellationToken);

        return new ImportWorkItemsReply
        {
            WorkItems = workItems.Select(workItem => new AzureDevOpsWorkItemDto
            {
                Id = workItem.Id,
                Title = workItem.Title,
                State = workItem.State,
                WorkItemType = workItem.WorkItemType,
                Revision = workItem.Revision,
                Estimate = workItem.Estimate,
                Description = workItem.Description
            }).ToList()
        };
    }

    public async Task<WriteEstimateReply> WriteEstimateAsync(WriteEstimateRequest request, CallContext context = default)
    {
        GuestAccessGuard.RejectGuests(currentUser);

        var result = await client.WriteEstimateAsync(
            new AzureDevOpsWriteEstimateRequest(request.WorkItemId, request.ExpectedRevision, request.Estimate),
            context.CancellationToken);

        return new WriteEstimateReply
        {
            WorkItemId = result.WorkItemId,
            Revision = result.Revision
        };
    }
}
