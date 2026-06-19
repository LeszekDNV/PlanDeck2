using Grpc.Core;
using PlanDeck.Application.Abstractions;
using PlanDeck.Application.Domain;
using PlanDeck.Core.Shared.Contracts;
using PlanDeck.Core.Shared.Validation;
using ProtoBuf.Grpc;

namespace PlanDeck.Application.Services;

public sealed class SessionGrpcService(ISessionRepository repository) : ISessionService
{
    private static readonly string[] FibonacciFaces = ["0", "1", "2", "3", "5", "8", "13", "21", "?", "☕"];

    private static readonly string[] TShirtFaces = ["XS", "S", "M", "L", "XL", "?", "☕"];

    public async Task<CreateSessionReply> CreateSessionAsync(CreateSessionRequest request, CallContext context = default)
    {
        var name = NormalizeName(request.Name);
        var scaleType = (VotingScaleType)(int)request.ScaleType;
        var scaleValues = ResolveScaleValues(scaleType, request.CustomScaleValues);

        var session = new PlanningSession
        {
            Name = name,
            TeamId = request.TeamId,
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

        var created = await repository.CreateSessionAsync(session, context.CancellationToken);
        return new CreateSessionReply { Session = ToDto(created) };
    }

    public async Task<ListSessionsReply> ListSessionsAsync(ListSessionsRequest request, CallContext context = default)
    {
        var sessions = await repository.GetSessionsAsync(context.CancellationToken);
        return new ListSessionsReply { Sessions = sessions.Select(ToDto).ToList() };
    }

    public async Task<GetSessionReply> GetSessionAsync(GetSessionRequest request, CallContext context = default)
    {
        try
        {
            var session = await LoadAsync(request.Id, context.CancellationToken);
            return new GetSessionReply { Session = ToDto(session) };
        }
        catch (SessionNotFoundException ex)
        {
            throw new RpcException(new Status(StatusCode.NotFound, ex.Message));
        }
    }

    public async Task<UpdateSessionConfigReply> UpdateSessionConfigAsync(UpdateSessionConfigRequest request, CallContext context = default)
    {
        var name = NormalizeName(request.Name);
        var scaleType = (VotingScaleType)(int)request.ScaleType;
        var scaleValues = ResolveScaleValues(scaleType, request.CustomScaleValues);

        try
        {
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
        try
        {
            var session = await LoadDraftAsync(request.SessionId, context.CancellationToken);

            var mapped = MapNewTask(request.Task, session.Tasks.Count == 0 ? 0 : session.Tasks.Max(t => t.SortOrder) + 1);
            if (IsDuplicateAdoTask(session.Tasks, mapped.AdoWorkItemId))
            {
                return new AddTaskReply { Session = ToDto(session) };
            }

            session.Tasks.Add(mapped);

            await repository.UpdateSessionAsync(session, context.CancellationToken);
            return new AddTaskReply { Session = ToDto(session) };
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

    public async Task<RemoveTaskReply> RemoveTaskAsync(RemoveTaskRequest request, CallContext context = default)
    {
        try
        {
            var session = await LoadDraftAsync(request.SessionId, context.CancellationToken);
            var task = session.Tasks.FirstOrDefault(t => t.Id == request.TaskId);
            if (task is not null)
            {
                session.Tasks.Remove(task);
                await repository.UpdateSessionAsync(session, context.CancellationToken);
            }

            return new RemoveTaskReply { Session = ToDto(session) };
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

    public async Task<ActivateSessionReply> ActivateSessionAsync(ActivateSessionRequest request, CallContext context = default)
    {
        try
        {
            var session = await LoadAsync(request.Id, context.CancellationToken);
            if (session.Status != SessionStatus.Active)
            {
                session.Status = SessionStatus.Active;
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
        var deleted = await repository.DeleteSessionAsync(request.Id, context.CancellationToken);
        return new DeleteSessionReply { Deleted = deleted };
    }

    private async Task<PlanningSession> LoadAsync(Guid id, CancellationToken cancellationToken)
    {
        return await repository.GetSessionAsync(id, cancellationToken)
            ?? throw new SessionNotFoundException(id);
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

    private static SessionTask MapNewTask(NewSessionTaskDto task, int sortOrder)
    {
        var title = task.Title?.Trim() ?? string.Empty;
        if (title.Length == 0)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, SessionValidationMessages.TaskTitleRequired));
        }

        return new SessionTask
        {
            Title = title,
            Source = (TaskSource)(int)task.Source,
            SortOrder = sortOrder,
            AdoWorkItemId = task.AdoWorkItemId,
            AdoRevision = task.AdoRevision,
            WorkItemType = task.WorkItemType,
            State = task.State
        };
    }

    private static SessionDto ToDto(PlanningSession session) => new()
    {
        Id = session.Id,
        Name = session.Name,
        TeamId = session.TeamId,
        Status = (SessionStatusDto)(int)session.Status,
        ScaleType = (VotingScaleTypeDto)(int)session.ScaleType,
        ScaleValues = [.. session.ScaleValues],
        CreatedAtUtc = session.CreatedAtUtc.UtcDateTime,
        Tasks = session.Tasks.OrderBy(t => t.SortOrder).Select(ToDto).ToList()
    };

    private static SessionTaskDto ToDto(SessionTask task) => new()
    {
        Id = task.Id,
        Title = task.Title,
        Source = (TaskSourceDto)(int)task.Source,
        SortOrder = task.SortOrder,
        AdoWorkItemId = task.AdoWorkItemId,
        AdoRevision = task.AdoRevision,
        WorkItemType = task.WorkItemType,
        State = task.State
    };
}
