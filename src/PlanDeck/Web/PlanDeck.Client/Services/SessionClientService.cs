using Grpc.Net.Client;
using PlanDeck.Core.Shared.Contracts;
using ProtoBuf.Grpc.Client;

namespace PlanDeck.Client.Services;

public sealed class SessionClientService(GrpcChannel channel) : ISessionClientService
{
    public async Task<IReadOnlyList<SessionDto>> GetSessionsAsync()
    {
        var service = channel.CreateGrpcService<ISessionService>();
        var reply = await service.ListSessionsAsync(new ListSessionsRequest());
        return reply.Sessions;
    }

    public async Task<SessionDto> GetSessionAsync(Guid id)
    {
        var service = channel.CreateGrpcService<ISessionService>();
        var reply = await service.GetSessionAsync(new GetSessionRequest { Id = id });
        return reply.Session;
    }

    public async Task<SessionDto> CreateSessionAsync(
        string name,
        Guid projectId,
        VotingScaleTypeDto scaleType,
        IReadOnlyList<string> customScaleValues,
        IReadOnlyList<NewSessionTaskDto> tasks,
        IReadOnlyList<int>? adoWorkItemIds = null)
    {
        var service = channel.CreateGrpcService<ISessionService>();
        var reply = await service.CreateSessionAsync(new CreateSessionRequest
        {
            Name = name,
            ProjectId = projectId,
            ScaleType = scaleType,
            CustomScaleValues = customScaleValues.ToList(),
            Tasks = tasks.Select(MapToAdHocTask).ToList(),
            AdoWorkItemIds = adoWorkItemIds?.ToList() ?? []
        });
        return reply.Session;
    }

    public async Task<SessionDto> AddAdoTasksAsync(Guid sessionId, IReadOnlyList<int> adoWorkItemIds)
    {
        var service = channel.CreateGrpcService<ISessionService>();
        var reply = await service.AddAdoTasksAsync(new AddAdoTasksRequest
        {
            SessionId = sessionId,
            AdoWorkItemIds = adoWorkItemIds.ToList()
        });
        return reply.Session;
    }

    public async Task<SessionDto> UpdateSessionConfigAsync(
        Guid id,
        string name,
        VotingScaleTypeDto scaleType,
        IReadOnlyList<string> customScaleValues)
    {
        var service = channel.CreateGrpcService<ISessionService>();
        var reply = await service.UpdateSessionConfigAsync(new UpdateSessionConfigRequest
        {
            Id = id,
            Name = name,
            ScaleType = scaleType,
            CustomScaleValues = customScaleValues.ToList()
        });
        return reply.Session;
    }

    public async Task<SessionDto> AddTaskAsync(Guid sessionId, NewSessionTaskDto task)
    {
        var service = channel.CreateGrpcService<ISessionService>();
        var reply = await service.AddTaskAsync(new AddTaskRequest
        {
            SessionId = sessionId,
            Task = MapToAdHocTask(task)
        });
        return reply.Session;
    }

    public async Task<SessionDto> AddTasksAsync(Guid sessionId, IReadOnlyList<NewSessionTaskDto> tasks)
    {
        var service = channel.CreateGrpcService<ISessionService>();
        var reply = await service.AddTasksAsync(new AddTasksRequest
        {
            SessionId = sessionId,
            Tasks = tasks.Select(MapToAdHocTask).ToList()
        });
        return reply.Session;
    }

    public async Task<SessionDto> UpdateTaskAsync(Guid sessionId, Guid taskId, string title, string? description)
    {
        var service = channel.CreateGrpcService<ISessionService>();
        var reply = await service.UpdateTaskAsync(new UpdateTaskRequest
        {
            SessionId = sessionId,
            TaskId = taskId,
            Title = title,
            Description = description
        });
        return reply.Session;
    }

    public async Task<SessionDto> RemoveTaskAsync(Guid sessionId, Guid taskId)
    {
        var service = channel.CreateGrpcService<ISessionService>();
        var reply = await service.RemoveTaskAsync(new RemoveTaskRequest { SessionId = sessionId, TaskId = taskId });
        return reply.Session;
    }

    public async Task<WriteTaskEstimateReply> WriteTaskEstimateToAdoAsync(Guid sessionId, Guid taskId)
    {
        var service = channel.CreateGrpcService<ISessionService>();
        return await service.WriteTaskEstimateToAdoAsync(new WriteTaskEstimateRequest
        {
            SessionId = sessionId,
            TaskId = taskId
        });
    }

    public async Task<SessionDto> ActivateSessionAsync(Guid id)
    {
        var service = channel.CreateGrpcService<ISessionService>();
        var reply = await service.ActivateSessionAsync(new ActivateSessionRequest { Id = id });
        return reply.Session;
    }

    public async Task<bool> DeleteSessionAsync(Guid id)
    {
        var service = channel.CreateGrpcService<ISessionService>();
        var reply = await service.DeleteSessionAsync(new DeleteSessionRequest { Id = id });
        return reply.Deleted;
    }

    private static NewAdHocTaskDto MapToAdHocTask(NewSessionTaskDto task) =>
        new()
        {
            Title = task.Title,
            Description = task.Description
        };
}
