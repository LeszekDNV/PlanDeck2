namespace PlanDeck.Infrastructure.AzureDevOps;

public sealed class AzureDevOpsOptions
{
    public const string SectionName = "AzureDevOps";

    public string OrganizationUrl { get; set; } = string.Empty;

    public string Project { get; set; } = string.Empty;

    public string EstimateField { get; set; } = "Microsoft.VSTS.Scheduling.StoryPoints";

    public string PersonalAccessToken { get; set; } = string.Empty;
}
