namespace PlanDeck.Application.Domain;

public sealed class AppUser : TenantEntity
{
    public Guid EntraObjectId { get; set; } = Guid.NewGuid();

    public required string DisplayName { get; set; }

    public required string Email { get; set; }

    public string NormalizedEmail { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
}
