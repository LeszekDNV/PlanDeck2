namespace PlanDeck.Application.Domain;

public sealed class SessionMember : TenantEntity
{
    public Guid SessionId { get; set; }

    public required string Email { get; set; }

    public string? DisplayName { get; set; }

    public Guid AssignedByUserId { get; set; }
}
