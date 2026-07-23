using System.Globalization;
using Grpc.Core;
using PlanDeck.Application.Abstractions;
using PlanDeck.Application.Domain;
using PlanDeck.Application.Planning;
using PlanDeck.Core.Shared.Contracts;
using PlanDeck.Core.Shared.Validation;
using ProtoBuf.Grpc;

namespace PlanDeck.Application.Services;

public sealed class SessionGrpcService(
    ISessionRepository repository,
    IProjectAzureDevOpsConnectionRepository projectConnectionRepository,
    ISessionMemberRepository memberRepository,
    ICurrentUserContext currentUser,
    IProjectAccessResolver projectAccessResolver,
    ISessionAccessResolver sessionAccessResolver,
    IPlanningRoomNotifier roomNotifier,
    IShareCodeGenerator shareCodeGenerator,
    IAzureDevOpsWorkItemClient azureDevOpsClient,
    IAdoConnectionContextResolver connectionResolver) : ISessionService
{
    private static readonly string[] FibonacciFaces = ["0", "1", "2", "3", "5", "8", "13", "21", "?", "☕"];

    private static readonly string[] TShirtFaces = ["XS", "S", "M", "L", "XL", "?", "☕"];

    public async Task<CreateSessionReply> CreateSessionAsync(CreateSessionRequest request, CallContext context = default)
    {
        GuestAccessGuard.RejectGuests(currentUser);

        var name = NormalizeName(request.Name);
        if (request.ProjectId == Guid.Empty)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "ProjectId is required."));
        }

        await RequireProjectRoleAsync(request.ProjectId, ProjectRole.Admin, context.CancellationToken);

        var scaleType = (VotingScaleType)(int)request.ScaleType;
        var scaleValues = ResolveScaleValues(scaleType, request.CustomScaleValues);

        var session = new PlanningSession
        {
            Name = name,
            ProjectId = request.ProjectId,
            Status = SessionStatus.Draft,
            ScaleType = scaleType,
            ScaleValues = scaleValues
        };

        var sortOrder = 0;
        foreach (var task in request.Tasks)
        {
            var mapped = MapNewTask(task, sortOrder);
            if (IsDuplicateAdoTask(session.Tasks, mapped.AdoWorkItemId))
            {
                continue;
            }

            session.Tasks.Add(mapped);
            sortOrder++;
        }

        var importedAdoTaskAdded = false;
        if (request.AdoWorkItemIds is { Count: > 0 })
        {
            var adoContext = await ResolveConnectionOrThrowAsync(request.ProjectId, context.CancellationToken);
            foreach (var workItemId in request.AdoWorkItemIds)
            {
                if (IsDuplicateAdoTask(session.Tasks, workItemId))
                {
                    continue;
                }

                var workItem = await azureDevOpsClient.GetWorkItemByIdAsync(adoContext, workItemId, context.CancellationToken);
                if (workItem is null)
                {
                    throw new RpcException(new Status(StatusCode.NotFound, $"ADO work item {workItemId} was not found."));
                }

                session.Tasks.Add(MapAdoWorkItem(workItem, sortOrder));
                sortOrder++;
                importedAdoTaskAdded = true;
            }
        }

        if (importedAdoTaskAdded)
        {
            await projectConnectionRepository.LockTargetAsync(request.ProjectId, context.CancellationToken);
        }

        var created = await repository.CreateSessionAsync(session, context.CancellationToken);
        await AddCreatorAsMemberAsync(created.Id, context.CancellationToken);
        return new CreateSessionReply { Session = ToDto(created) };
    }

    private async Task AddCreatorAsMemberAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var email = currentUser.Email?.Trim();
        if (string.IsNullOrEmpty(email) || !EmailValidator.IsValid(email))
        {
            return;
        }

        var displayName = string.IsNullOrWhiteSpace(currentUser.DisplayName) ? null : currentUser.DisplayName.Trim();
        try
        {
            await memberRepository.AssignMemberAsync(sessionId, email, displayName, cancellationToken);
        }
        catch (DuplicateSessionMemberException)
        {
            // Creator already present as a member; nothing to do.
        }
    }

    public async Task<ListSessionsReply> ListSessionsAsync(ListSessionsRequest request, CallContext context = default)
    {
        GuestAccessGuard.RejectGuests(currentUser);

        if (request.ProjectId == Guid.Empty)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "ProjectId is required."));
        }

        await RequireProjectRoleAsync(
            request.ProjectId,
            ProjectRole.Member,
            context.CancellationToken,
            concealPermissionDeniedAsNotFound: true);

        var sessions = await repository.GetSessionsAsync(request.ProjectId, context.CancellationToken);
        return new ListSessionsReply { Sessions = sessions.Select(ToDto).ToList() };
    }

    public async Task<GetSessionReply> GetSessionAsync(GetSessionRequest request, CallContext context = default)
    {
        // Guests may read only the single session their share-link cookie is scoped to; any other
        // id in the tenant is off-limits even though the tenant filter would otherwise resolve it.
        if (currentUser.IsGuest)
        {
            if (request.Id != currentUser.SessionScope)
            {
                throw new RpcException(new Status(StatusCode.PermissionDenied, "Guests can only access their own session."));
            }

            try
            {
                var scopedSession = await LoadAsync(request.Id, context.CancellationToken);
                return new GetSessionReply { Session = ToDto(scopedSession) };
            }
            catch (SessionNotFoundException ex)
            {
                throw new RpcException(new Status(StatusCode.NotFound, ex.Message));
            }
        }

        try
        {
            await RequireSessionRoleAsync(request.Id, ProjectRole.Member, context.CancellationToken);
            var session = await LoadAsync(request.Id, context.CancellationToken);
            return new GetSessionReply { Session = ToDto(session) };
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            var session = await repository.GetSessionAsync(request.Id, context.CancellationToken);
            if (session is { Status: SessionStatus.Active }
                && await IsCurrentUserAssignedToSessionAsync(request.Id, context.CancellationToken))
            {
                return new GetSessionReply { Session = ToDto(session) };
            }

            throw;
        }
        catch (SessionNotFoundException ex)
        {
            throw new RpcException(new Status(StatusCode.NotFound, ex.Message));
        }
    }

    public async Task<UpdateSessionConfigReply> UpdateSessionConfigAsync(UpdateSessionConfigRequest request, CallContext context = default)
    {
        GuestAccessGuard.RejectGuests(currentUser);

        var name = NormalizeName(request.Name);
        var scaleType = (VotingScaleType)(int)request.ScaleType;
        var scaleValues = ResolveScaleValues(scaleType, request.CustomScaleValues);

        try
        {
            await RequireSessionRoleAsync(request.Id, ProjectRole.Admin, context.CancellationToken);
            var session = await LoadDraftAsync(request.Id, context.CancellationToken);
            session.Name = name;
            session.ScaleType = scaleType;
            session.ScaleValues = scaleValues;

            await repository.UpdateSessionAsync(session, context.CancellationToken);
            return new UpdateSessionConfigReply { Session = ToDto(session) };
        }
        catch (SessionNotFoundException ex)
        {
            throw new RpcException(new Status(StatusCode.NotFound, ex.Message));
        }
        catch (SessionNotDraftException ex)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, ex.Message));
        }
    }

    public async Task<AddTaskReply> AddTaskAsync(AddTaskRequest request, CallContext context = default)
    {
        GuestAccessGuard.RejectGuests(currentUser);

        try
        {
            await RequireSessionRoleAsync(request.SessionId, ProjectRole.Admin, context.CancellationToken);
            var session = await LoadEditableAsync(request.SessionId, context.CancellationToken);

            var mapped = MapNewTask(request.Task, session.Tasks.Count == 0 ? 0 : session.Tasks.Max(t => t.SortOrder) + 1);
            if (IsDuplicateAdoTask(session.Tasks, mapped.AdoWorkItemId))
            {
                return new AddTaskReply { Session = ToDto(session) };
            }

            session.Tasks.Add(mapped);

            await repository.UpdateSessionAsync(session, context.CancellationToken);
            await NotifyIfActiveAsync(session, context.CancellationToken);
            return new AddTaskReply { Session = ToDto(session) };
        }
        catch (SessionNotFoundException ex)
        {
            throw new RpcException(new Status(StatusCode.NotFound, ex.Message));
        }
    }

    public async Task<AddTasksReply> AddTasksAsync(AddTasksRequest request, CallContext context = default)
    {
        GuestAccessGuard.RejectGuests(currentUser);

        try
        {
            await RequireSessionRoleAsync(request.SessionId, ProjectRole.Admin, context.CancellationToken);
            var session = await LoadEditableAsync(request.SessionId, context.CancellationToken);

            var sortOrder = session.Tasks.Count == 0 ? 0 : session.Tasks.Max(t => t.SortOrder) + 1;
            var added = false;
            foreach (var task in request.Tasks)
            {
                var mapped = MapNewTask(task, sortOrder);
                if (IsDuplicateAdoTask(session.Tasks, mapped.AdoWorkItemId))
                {
                    continue;
                }

                session.Tasks.Add(mapped);
                sortOrder++;
                added = true;
            }

            if (added)
            {
                await projectConnectionRepository.LockTargetAsync(session.ProjectId, context.CancellationToken);
                await repository.UpdateSessionAsync(session, context.CancellationToken);
                await NotifyIfActiveAsync(session, context.CancellationToken);
            }

            return new AddTasksReply { Session = ToDto(session) };
        }
        catch (SessionNotFoundException ex)
        {
            throw new RpcException(new Status(StatusCode.NotFound, ex.Message));
        }
    }

    public async Task<UpdateTaskReply> UpdateTaskAsync(UpdateTaskRequest request, CallContext context = default)
    {
        GuestAccessGuard.RejectGuests(currentUser);

        var title = request.Title?.Trim() ?? string.Empty;
        if (title.Length == 0)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, SessionValidationMessages.TaskTitleRequired));
        }

        try
        {
            await RequireSessionRoleAsync(request.SessionId, ProjectRole.Admin, context.CancellationToken);
            var session = await LoadEditableAsync(request.SessionId, context.CancellationToken);
            var task = session.Tasks.FirstOrDefault(t => t.Id == request.TaskId)
                ?? throw new SessionTaskNotFoundException(request.TaskId);

            task.Title = title;
            task.Description = NormalizeDescription(request.Description);

            await repository.UpdateSessionAsync(session, context.CancellationToken);
            await NotifyIfActiveAsync(session, context.CancellationToken);
            return new UpdateTaskReply { Session = ToDto(session) };
        }
        catch (SessionNotFoundException ex)
        {
            throw new RpcException(new Status(StatusCode.NotFound, ex.Message));
        }
        catch (SessionTaskNotFoundException ex)
        {
            throw new RpcException(new Status(StatusCode.NotFound, ex.Message));
        }
    }

    public async Task<RemoveTaskReply> RemoveTaskAsync(RemoveTaskRequest request, CallContext context = default)
    {
        GuestAccessGuard.RejectGuests(currentUser);

        try
        {
            await RequireSessionRoleAsync(request.SessionId, ProjectRole.Admin, context.CancellationToken);
            var session = await LoadEditableAsync(request.SessionId, context.CancellationToken);
            var task = session.Tasks.FirstOrDefault(t => t.Id == request.TaskId);
            if (task is not null)
            {
                session.Tasks.Remove(task);
                await repository.UpdateSessionAsync(session, context.CancellationToken);
                await NotifyIfActiveAsync(session, context.CancellationToken);
            }

            return new RemoveTaskReply { Session = ToDto(session) };
        }
        catch (SessionNotFoundException ex)
        {
            throw new RpcException(new Status(StatusCode.NotFound, ex.Message));
        }
    }

    public async Task<WriteTaskEstimateReply> WriteTaskEstimateToAdoAsync(WriteTaskEstimateRequest request, CallContext context = default)
    {
        GuestAccessGuard.RejectGuests(currentUser);

        PlanningSession session;
        try
        {
            await RequireSessionRoleAsync(request.SessionId, ProjectRole.Admin, context.CancellationToken);
            session = await LoadAsync(request.SessionId, context.CancellationToken);
        }
        catch (SessionNotFoundException ex)
        {
            throw new RpcException(new Status(StatusCode.NotFound, ex.Message));
        }

        var task = session.Tasks.FirstOrDefault(t => t.Id == request.TaskId)
            ?? throw new RpcException(new Status(StatusCode.NotFound, $"Task '{request.TaskId}' was not found in the session."));

        if (task.Source != TaskSource.AzureDevOps || task.AdoWorkItemId is null)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, "Task is not linked to an Azure DevOps work item."));
        }

        if (string.IsNullOrWhiteSpace(task.AgreedEstimate))
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, "Task has no agreed estimate to write back."));
        }

        if (!double.TryParse(task.AgreedEstimate, NumberStyles.Any, CultureInfo.InvariantCulture, out var estimate))
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, "Agreed estimate is not numeric and cannot be written to Azure DevOps."));
        }

        AzureDevOpsWriteEstimateResult result;
        AdoConnectionContext adoContext;
        try
        {
            adoContext = await connectionResolver.ResolveAsync(session.ProjectId, context.CancellationToken);
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

        try
        {
            result = await azureDevOpsClient.WriteEstimateAsync(
                adoContext,
                new AzureDevOpsWriteEstimateRequest(task.AdoWorkItemId.Value, task.AdoRevision, estimate),
                context.CancellationToken);
        }
        catch (AzureDevOpsConcurrencyException)
        {
            throw new RpcException(new Status(StatusCode.Aborted, "Azure DevOps work item revision changed before write-back completed."));
        }
        catch (AzureDevOpsRateLimitException)
        {
            throw new RpcException(new Status(StatusCode.ResourceExhausted, "Azure DevOps rate limit reached. Retry the write-back shortly."));
        }
        catch (Exception)
        {
            throw new RpcException(new Status(StatusCode.Unavailable, "Azure DevOps write-back failed."));
        }

        await repository.SetAdoRevisionAsync(request.SessionId, request.TaskId, result.Revision, context.CancellationToken);
        task.AdoRevision = result.Revision;

        return new WriteTaskEstimateReply
        {
            Session = ToDto(session),
            WorkItemId = result.WorkItemId,
            Revision = result.Revision
        };
    }

    public async Task<ActivateSessionReply> ActivateSessionAsync(ActivateSessionRequest request, CallContext context = default)
    {
        GuestAccessGuard.RejectGuests(currentUser);

        try
        {
            await RequireSessionRoleAsync(request.Id, ProjectRole.Admin, context.CancellationToken);
            var session = await LoadAsync(request.Id, context.CancellationToken);
            if (session.Status != SessionStatus.Active)
            {
                session.Status = SessionStatus.Active;
                // Mint the share code on first activation so guests can join via link; once set it
                // is stable for the life of the session. Regenerate on the rare collision so a clash
                // surfaces as a fresh code rather than a failed activation.
                session.ShareCode ??= await GenerateUniqueShareCodeAsync(context.CancellationToken);
                await repository.UpdateSessionAsync(session, context.CancellationToken);
            }

            return new ActivateSessionReply { Session = ToDto(session) };
        }
        catch (SessionNotFoundException ex)
        {
            throw new RpcException(new Status(StatusCode.NotFound, ex.Message));
        }
    }

    public async Task<DeleteSessionReply> DeleteSessionAsync(DeleteSessionRequest request, CallContext context = default)
    {
        GuestAccessGuard.RejectGuests(currentUser);

        await RequireSessionRoleAsync(request.Id, ProjectRole.Admin, context.CancellationToken);
        var deleted = await repository.DeleteSessionAsync(request.Id, context.CancellationToken);
        return new DeleteSessionReply { Deleted = deleted };
    }

    public async Task<AddAdoTasksReply> AddAdoTasksAsync(AddAdoTasksRequest request, CallContext context = default)
    {
        GuestAccessGuard.RejectGuests(currentUser);

        if (request.AdoWorkItemIds is null or { Count: 0 })
        {
            PlanningSession empty;
            try
            {
                await RequireSessionRoleAsync(request.SessionId, ProjectRole.Admin, context.CancellationToken);
                empty = await LoadEditableAsync(request.SessionId, context.CancellationToken);
            }
            catch (SessionNotFoundException ex)
            {
                throw new RpcException(new Status(StatusCode.NotFound, ex.Message));
            }

            return new AddAdoTasksReply { Session = ToDto(empty) };
        }

        PlanningSession session;
        try
        {
            await RequireSessionRoleAsync(request.SessionId, ProjectRole.Admin, context.CancellationToken);
            session = await LoadEditableAsync(request.SessionId, context.CancellationToken);
        }
        catch (SessionNotFoundException ex)
        {
            throw new RpcException(new Status(StatusCode.NotFound, ex.Message));
        }

        var adoContext = await ResolveConnectionOrThrowAsync(session.ProjectId, context.CancellationToken);

        var sortOrder = session.Tasks.Count == 0 ? 0 : session.Tasks.Max(t => t.SortOrder) + 1;
        var added = false;

        foreach (var workItemId in request.AdoWorkItemIds)
        {
            if (IsDuplicateAdoTask(session.Tasks, workItemId))
            {
                continue;
            }

            var workItem = await azureDevOpsClient.GetWorkItemByIdAsync(adoContext, workItemId, context.CancellationToken);
            if (workItem is null)
            {
                throw new RpcException(new Status(StatusCode.NotFound, $"ADO work item {workItemId} was not found."));
            }

            session.Tasks.Add(MapAdoWorkItem(workItem, sortOrder));
            sortOrder++;
            added = true;
        }

        if (added)
        {
            await projectConnectionRepository.LockTargetAsync(session.ProjectId, context.CancellationToken);
            await repository.UpdateSessionAsync(session, context.CancellationToken);
            await NotifyIfActiveAsync(session, context.CancellationToken);
        }

        return new AddAdoTasksReply { Session = ToDto(session) };
    }

    private async Task<bool> IsCurrentUserAssignedToSessionAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var email = currentUser.Email?.Trim();
        if (string.IsNullOrEmpty(email) || !EmailValidator.IsValid(email))
        {
            return false;
        }

        var members = await memberRepository.GetMembersAsync(sessionId, cancellationToken);
        return members.Any(member => string.Equals(member.Email, email, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<PlanningSession> LoadAsync(Guid id, CancellationToken cancellationToken)
    {
        return await repository.GetSessionAsync(id, cancellationToken)
            ?? throw new SessionNotFoundException(id);
    }

    private async Task<string> GenerateUniqueShareCodeAsync(CancellationToken cancellationToken)
    {
        const int maxAttempts = 5;
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            var code = shareCodeGenerator.Generate();
            if (!await repository.ShareCodeExistsAsync(code, cancellationToken))
            {
                return code;
            }
        }

        throw new RpcException(new Status(
            StatusCode.Internal,
            "Could not allocate a unique share code; please retry."));
    }

    private async Task<PlanningSession> LoadDraftAsync(Guid id, CancellationToken cancellationToken)
    {
        var session = await LoadAsync(id, cancellationToken);
        if (session.Status != SessionStatus.Draft)
        {
            throw new SessionNotDraftException(id);
        }

        return session;
    }

    private async Task<PlanningSession> LoadEditableAsync(Guid id, CancellationToken cancellationToken)
        => await LoadAsync(id, cancellationToken);

    private async Task RequireSessionRoleAsync(Guid sessionId, ProjectRole minimumRole, CancellationToken cancellationToken)
    {
        var access = await sessionAccessResolver.ResolveProjectAccessAsync(sessionId, cancellationToken);
        if (access is null)
        {
            throw new RpcException(new Status(StatusCode.NotFound, $"Session '{sessionId}' was not found."));
        }

        if (access.Value.Role < minimumRole)
        {
            throw new RpcException(new Status(StatusCode.PermissionDenied, $"Session '{sessionId}' requires the '{minimumRole}' role."));
        }
    }

    private async Task RequireProjectRoleAsync(
        Guid projectId,
        ProjectRole minimumRole,
        CancellationToken cancellationToken,
        bool concealPermissionDeniedAsNotFound = false)
    {
        try
        {
            _ = await projectAccessResolver.RequireRoleAsync(projectId, minimumRole, cancellationToken);
        }
        catch (ProjectNotFoundException ex)
        {
            throw new RpcException(new Status(StatusCode.NotFound, ex.Message));
        }
        catch (ProjectPermissionDeniedException ex)
        {
            if (concealPermissionDeniedAsNotFound)
            {
                throw new RpcException(new Status(StatusCode.NotFound, $"Project '{projectId}' was not found."));
            }

            throw new RpcException(new Status(StatusCode.PermissionDenied, ex.Message));
        }
    }

    private async Task NotifyIfActiveAsync(PlanningSession session, CancellationToken cancellationToken)
    {
        if (session.Status != SessionStatus.Active)
        {
            return;
        }

        var snapshots = session.Tasks
            .OrderBy(t => t.SortOrder)
            .Select(t => new PlanningRoomTaskSnapshot(t.Id, t.Title, t.Description, t.SortOrder, t.AgreedEstimate))
            .ToList();

        await roomNotifier.NotifyTasksChangedAsync(session.Id, snapshots, cancellationToken);
    }

    private static string NormalizeName(string? name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, SessionValidationMessages.NameRequired));
        }

        return trimmed;
    }

    private static List<string> ResolveScaleValues(VotingScaleType scaleType, List<string>? customValues) => scaleType switch
    {
        VotingScaleType.Fibonacci => [.. FibonacciFaces],
        VotingScaleType.TShirt => [.. TShirtFaces],
        VotingScaleType.Custom => ResolveCustomScaleValues(customValues),
        _ => throw new RpcException(new Status(StatusCode.InvalidArgument, SessionValidationMessages.UnknownScaleType))
    };

    private static List<string> ResolveCustomScaleValues(List<string>? customValues)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var values = new List<string>();
        foreach (var raw in customValues ?? [])
        {
            var value = raw?.Trim() ?? string.Empty;
            if (value.Length == 0)
            {
                continue;
            }

            if (seen.Add(value))
            {
                values.Add(value);
            }
        }

        if (values.Count == 0)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, SessionValidationMessages.CustomScaleRequired));
        }

        return values;
    }

    private static bool IsDuplicateAdoTask(IEnumerable<SessionTask> tasks, int? adoWorkItemId) =>
        adoWorkItemId is int id && tasks.Any(t => t.AdoWorkItemId == id);

    private static string? NormalizeDescription(string? description)
    {
        var trimmed = description?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static SessionTask MapAdoWorkItem(AzureDevOpsWorkItem workItem, int sortOrder)
        => new()
        {
            Title = workItem.Title,
            Description = string.IsNullOrWhiteSpace(workItem.Description) ? null : workItem.Description.Trim(),
            Source = TaskSource.AzureDevOps,
            SortOrder = sortOrder,
            AdoWorkItemId = workItem.Id,
            AdoRevision = workItem.Revision,
            WorkItemType = workItem.WorkItemType,
            State = workItem.State
        };

    private async Task<AdoConnectionContext> ResolveConnectionOrThrowAsync(Guid projectId, CancellationToken cancellationToken)
    {
        try
        {
            return await connectionResolver.ResolveAsync(projectId, cancellationToken);
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
    }

    private static SessionTask MapNewTask(NewAdHocTaskDto task, int sortOrder)
    {
        var title = task.Title?.Trim() ?? string.Empty;
        if (title.Length == 0)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, SessionValidationMessages.TaskTitleRequired));
        }

        return new SessionTask
        {
            Title = title,
            Description = NormalizeDescription(task.Description),
            Source = TaskSource.AdHoc,
            SortOrder = sortOrder,
        };
    }

    private static SessionDto ToDto(PlanningSession session) => new()
    {
        Id = session.Id,
        Name = session.Name,
        ProjectId = session.ProjectId,
        Status = (SessionStatusDto)(int)session.Status,
        ScaleType = (VotingScaleTypeDto)(int)session.ScaleType,
        ScaleValues = [.. session.ScaleValues],
        CreatedAtUtc = session.CreatedAtUtc.UtcDateTime,
        Tasks = session.Tasks.OrderBy(t => t.SortOrder).Select(ToDto).ToList(),
        ShareCode = session.ShareCode
    };

    private static SessionTaskDto ToDto(SessionTask task) => new()
    {
        Id = task.Id,
        Title = task.Title,
        Description = task.Description,
        Source = (TaskSourceDto)(int)task.Source,
        SortOrder = task.SortOrder,
        AdoWorkItemId = task.AdoWorkItemId,
        AdoRevision = task.AdoRevision,
        WorkItemType = task.WorkItemType,
        State = task.State,
        AgreedEstimate = task.AgreedEstimate
    };
}


