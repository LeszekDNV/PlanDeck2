using PlanDeck.Core.Shared.Contracts;

namespace PlanDeck.Client.Services;

public interface ISessionClientService
{
    Task<IReadOnlyList<SessionDto>> GetSessionsAsync(Guid projectId);

    Task<SessionDto> GetSessionAsync(Guid id);

    Task<SessionDto> CreateSessionAsync(
        string name,
        Guid projectId,
        VotingScaleTypeDto scaleType,
        IReadOnlyList<string> customScaleValues,
        IReadOnlyList<NewSessionTaskDto> tasks,
        IReadOnlyList<int>? adoWorkItemIds = null);

    Task<SessionDto> UpdateSessionConfigAsync(
        Guid id,
        string name,
        VotingScaleTypeDto scaleType,
        IReadOnlyList<string> customScaleValues);

    Task<SessionDto> AddTaskAsync(Guid sessionId, NewSessionTaskDto task);

    Task<SessionDto> AddTasksAsync(Guid sessionId, IReadOnlyList<NewSessionTaskDto> tasks);

    /// <summary>
    /// Adds ADO work items to an existing session by ID only.
    /// Server re-fetches authoritative metadata for each ID.
    /// </summary>
    Task<SessionDto> AddAdoTasksAsync(Guid sessionId, IReadOnlyList<int> adoWorkItemIds);

    Task<SessionDto> UpdateTaskAsync(Guid sessionId, Guid taskId, string title, string? description);

    Task<SessionDto> RemoveTaskAsync(Guid sessionId, Guid taskId);

    Task<WriteTaskEstimateReply> WriteTaskEstimateToAdoAsync(Guid sessionId, Guid taskId);

    Task<SessionDto> ActivateSessionAsync(Guid id);

    Task<bool> DeleteSessionAsync(Guid id);
}
