namespace PlanDeck.Application.Abstractions;

/// <summary>
/// Resolves the per-request ADO connection context from a project's stored connection and Key Vault secret.
/// </summary>
public interface IAdoConnectionContextResolver
{
    Task<AdoConnectionContext> ResolveAsync(Guid projectId, CancellationToken cancellationToken);
}

public sealed class ProjectConnectionNotFoundException(Guid projectId)
    : Exception($"No ADO connection configured for project '{projectId}'.")
{
    public Guid ProjectId { get; } = projectId;
}

public sealed class ProjectConnectionDisabledException(Guid projectId)
    : Exception($"The ADO connection for project '{projectId}' is disabled.")
{
    public Guid ProjectId { get; } = projectId;
}
