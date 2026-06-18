namespace PlanDeck.Application.Domain;

public sealed class TeamMember : TenantEntity
{
    public Guid TeamId { get; set; }

    public required string Email { get; set; }

    public string? DisplayName { get; set; }

    public Guid InvitedByUserId { get; set; }
}
