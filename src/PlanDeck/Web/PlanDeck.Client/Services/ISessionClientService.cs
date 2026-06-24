using PlanDeck.Core.Shared.Contracts;

namespace PlanDeck.Client.Services;

public interface ISessionClientService
{
    Task<IReadOnlyList<SessionDto>> GetSessionsAsync();

    Task<SessionDto> GetSessionAsync(Guid id);

    Task<SessionDto> CreateSessionAsync(
        string name,
        Guid? teamId,
        VotingScaleTypeDto scaleType,
        IReadOnlyList<string> customScaleValues,
        IReadOnlyList<NewSessionTaskDto> tasks);

    Task<SessionDto> UpdateSessionConfigAsync(
        Guid id,
        string name,
        Guid? teamId,
        VotingScaleTypeDto scaleType,
        IReadOnlyList<string> customScaleValues);

    Task<SessionDto> AddTaskAsync(Guid sessionId, NewSessionTaskDto task);

    Task<SessionDto> AddTasksAsync(Guid sessionId, IReadOnlyList<NewSessionTaskDto> tasks);

    Task<SessionDto> UpdateTaskAsync(Guid sessionId, Guid taskId, string title, string? description);

    Task<SessionDto> RemoveTaskAsync(Guid sessionId, Guid taskId);

    Task<WriteTaskEstimateReply> WriteTaskEstimateToAdoAsync(Guid sessionId, Guid taskId);

    Task<SessionDto> ActivateSessionAsync(Guid id);

    Task<bool> DeleteSessionAsync(Guid id);
}
