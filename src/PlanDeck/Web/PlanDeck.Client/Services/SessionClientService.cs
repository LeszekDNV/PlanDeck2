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
        Guid? teamId,
        VotingScaleTypeDto scaleType,
        IReadOnlyList<string> customScaleValues,
        IReadOnlyList<NewSessionTaskDto> tasks)
    {
        var service = channel.CreateGrpcService<ISessionService>();
        var reply = await service.CreateSessionAsync(new CreateSessionRequest
        {
            Name = name,
            TeamId = teamId,
            ScaleType = scaleType,
            CustomScaleValues = customScaleValues.ToList(),
            Tasks = tasks.ToList()
        });
        return reply.Session;
    }

    public async Task<SessionDto> UpdateSessionConfigAsync(
        Guid id,
        string name,
        Guid? teamId,
        VotingScaleTypeDto scaleType,
        IReadOnlyList<string> customScaleValues)
    {
        var service = channel.CreateGrpcService<ISessionService>();
        var reply = await service.UpdateSessionConfigAsync(new UpdateSessionConfigRequest
        {
            Id = id,
            Name = name,
            TeamId = teamId,
            ScaleType = scaleType,
            CustomScaleValues = customScaleValues.ToList()
        });
        return reply.Session;
    }

    public async Task<SessionDto> AddTaskAsync(Guid sessionId, NewSessionTaskDto task)
    {
        var service = channel.CreateGrpcService<ISessionService>();
        var reply = await service.AddTaskAsync(new AddTaskRequest { SessionId = sessionId, Task = task });
        return reply.Session;
    }

    public async Task<SessionDto> RemoveTaskAsync(Guid sessionId, Guid taskId)
    {
        var service = channel.CreateGrpcService<ISessionService>();
        var reply = await service.RemoveTaskAsync(new RemoveTaskRequest { SessionId = sessionId, TaskId = taskId });
        return reply.Session;
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
}
