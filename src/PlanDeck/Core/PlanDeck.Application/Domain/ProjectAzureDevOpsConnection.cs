namespace PlanDeck.Application.Domain;

public sealed class ProjectAzureDevOpsConnection : TenantEntity
{
    public Guid ProjectId { get; set; }

    public required string OrganizationUrl { get; set; }

    public required string AzureDevOpsProject { get; set; }

    public required string EstimateField { get; set; }

    public required string DescriptionField { get; set; }

    public required string ReproStepsField { get; set; }

    public required string AcceptanceCriteriaField { get; set; }

    public required string SecretName { get; set; }

    public bool IsEnabled { get; set; } = true;

    public ConnectionValidationState ValidationState { get; set; }

    public DateTimeOffset? LastValidatedAtUtc { get; set; }

    public DateTimeOffset? TargetLockedAtUtc { get; set; }

    public void UpdateSettings(
        string organizationUrl,
        string azureDevOpsProject,
        string estimateField,
        string descriptionField,
        string reproStepsField,
        string acceptanceCriteriaField,
        DateTimeOffset validatedAtUtc)
    {
        if (TargetLockedAtUtc.HasValue
            && (!string.Equals(OrganizationUrl, organizationUrl, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(AzureDevOpsProject, azureDevOpsProject, StringComparison.Ordinal)))
        {
            throw new ProjectConnectionTargetLockedException();
        }

        OrganizationUrl = organizationUrl;
        AzureDevOpsProject = azureDevOpsProject;
        EstimateField = estimateField;
        DescriptionField = descriptionField;
        ReproStepsField = reproStepsField;
        AcceptanceCriteriaField = acceptanceCriteriaField;
        MarkValidated(validatedAtUtc);
    }

    public void MarkValidated(DateTimeOffset validatedAtUtc)
    {
        ValidationState = ConnectionValidationState.Valid;
        LastValidatedAtUtc = validatedAtUtc;
    }
}

public enum ConnectionValidationState
{
    NotValidated = 0,
    Valid = 1,
    Invalid = 2
}

public sealed class ProjectConnectionTargetLockedException()
    : Exception("The Azure DevOps organization and project are locked.");
