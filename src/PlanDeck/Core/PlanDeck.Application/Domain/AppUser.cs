namespace PlanDeck.Application.Domain;

public sealed class AppUser : TenantEntity
{
    public required string FirstName { get; set; }

    public required string LastName { get; set; }

    public bool IsActive { get; set; } = true;

    public TenantRole Role { get; set; }
}
