namespace PlanDeck.Application.Domain;

public sealed class ProjectTeam : TenantEntity
{
    public Guid ProjectId { get; set; }

    public Guid TeamId { get; set; }

    public Guid AssignedByUserId { get; set; }
}
