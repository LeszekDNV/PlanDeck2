using PlanDeck.Application.Domain;

namespace PlanDeck.Application.Abstractions;

public interface IProjectAccessResolver
{
    Task<ProjectRole?> GetEffectiveRoleAsync(
        Guid projectId,
        CancellationToken cancellationToken);

    Task<ProjectRole> RequireRoleAsync(
        Guid projectId,
        ProjectRole minimumRole,
        CancellationToken cancellationToken);
}

public sealed class ProjectNotFoundException(Guid projectId)
    : Exception($"Project '{projectId}' was not found.")
{
    public Guid ProjectId { get; } = projectId;
}

public sealed class ProjectPermissionDeniedException(Guid projectId, ProjectRole requiredRole)
    : Exception($"Project '{projectId}' requires the '{requiredRole}' role.")
{
    public Guid ProjectId { get; } = projectId;

    public ProjectRole RequiredRole { get; } = requiredRole;
}
