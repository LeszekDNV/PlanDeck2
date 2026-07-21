namespace PlanDeck.Application.Domain;

public sealed class PlanDeckProject : TenantEntity
{
    public required string Name { get; set; }

    public string? Description { get; set; }

    public Guid CreatedByUserId { get; set; }
}
