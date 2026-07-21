using PlanDeck.Application.Domain;

namespace PlanDeck.Application.Abstractions;

public interface IProjectAzureDevOpsConnectionRepository
{
    Task<ProjectAzureDevOpsConnection?> GetAsync(
        Guid projectId,
        CancellationToken cancellationToken);

    Task AddAsync(
        ProjectAzureDevOpsConnection connection,
        CancellationToken cancellationToken);

    Task UpdateAsync(
        ProjectAzureDevOpsConnection connection,
        CancellationToken cancellationToken);

    Task DeleteAsync(
        ProjectAzureDevOpsConnection connection,
        CancellationToken cancellationToken);

    Task LockTargetAsync(Guid projectId, CancellationToken cancellationToken);
}

public sealed class ProjectConnectionPersistenceException()
    : Exception("The project connection metadata could not be persisted.");
