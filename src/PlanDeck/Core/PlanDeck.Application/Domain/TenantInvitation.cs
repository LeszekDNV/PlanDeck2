namespace PlanDeck.Application.Domain;

public sealed class TenantInvitation : TenantEntity
{
    public required byte[] TokenHash { get; set; }

    public required string NormalizedEmail { get; set; }

    public TenantRole Role { get; set; }

    public InvitationStatus Status { get; set; } = InvitationStatus.Pending;

    public DateTimeOffset ExpiresAtUtc { get; set; }

    public DateTimeOffset? AcceptedAtUtc { get; set; }
}

