namespace PlanDeck.Application.Domain;

public sealed class AppUser : TenantEntity
{
    public required string DisplayName { get; set; }

    public required string Email { get; set; }
}
