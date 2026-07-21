using Grpc.Core;
using PlanDeck.Application.Abstractions;
using PlanDeck.Application.Domain;
using PlanDeck.Core.Shared.AzureDevOps;
using PlanDeck.Core.Shared.Contracts;
using ProtoBuf.Grpc;

namespace PlanDeck.Application.Services;

public sealed class AzureDevOpsWorkItemGrpcService(
    IAzureDevOpsWorkItemClient client,
    ICurrentUserContext currentUser,
    IProjectAccessResolver access,
    IAdoConnectionContextResolver connectionResolver) : IAzureDevOpsWorkItemService
{
    public async Task<ImportWorkItemsReply> ImportWorkItemsAsync(ImportWorkItemsRequest request, CallContext context = default)
    {
        GuestAccessGuard.RejectGuests(currentUser);

        if (request.ProjectId == Guid.Empty)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "ProjectId is required for ADO import."));
        }

        try
        {
            await access.RequireRoleAsync(request.ProjectId, ProjectRole.Member, context.CancellationToken);
        }
        catch (ProjectNotFoundException)
        {
            throw new RpcException(new Status(StatusCode.NotFound, $"Project '{request.ProjectId}' was not found."));
        }
        catch (ProjectPermissionDeniedException)
        {
            throw new RpcException(new Status(StatusCode.PermissionDenied, "You do not have access to this project's ADO connection."));
        }

        AdoConnectionContext adoContext;
        try
        {
            adoContext = await connectionResolver.ResolveAsync(request.ProjectId, context.CancellationToken);
        }
        catch (ProjectConnectionNotFoundException)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, "No ADO connection is configured for this project."));
        }
        catch (ProjectConnectionDisabledException)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, "The project ADO connection is disabled."));
        }
        catch (ProjectSecretStoreException)
        {
            throw new RpcException(new Status(StatusCode.Internal, "Failed to resolve ADO credentials."));
        }

        var whereClause = AzureDevOpsWiqlBuilder.BuildWhereClause(request.WorkItemTypes, request.States);

        var workItems = await client.ImportWorkItemsAsync(
            adoContext,
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
}
