using PlanDeck.Application.Abstractions;

namespace PlanDeck.Infrastructure.AzureDevOps;

public sealed class AdoConnectionContextResolver(
    IProjectAzureDevOpsConnectionRepository repo,
    IProjectSecretStore secrets) : IAdoConnectionContextResolver
{
    public async Task<AdoConnectionContext> ResolveAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var connection = await repo.GetAsync(projectId, cancellationToken)
            ?? throw new ProjectConnectionNotFoundException(projectId);

        if (!connection.IsEnabled)
            throw new ProjectConnectionDisabledException(projectId);

        var pat = await secrets.GetLatestAsync(connection.SecretName, cancellationToken);

        return new AdoConnectionContext(
            connection.OrganizationUrl,
            connection.AzureDevOpsProject,
            pat,
            connection.EstimateField,
            connection.DescriptionField,
            connection.ReproStepsField,
            connection.AcceptanceCriteriaField);
    }
}
