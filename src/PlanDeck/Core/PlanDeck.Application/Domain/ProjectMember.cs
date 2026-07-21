namespace PlanDeck.Application.Domain;

public sealed class ProjectMember : TenantEntity
{
    public Guid ProjectId { get; set; }

    public Guid? AppUserId { get; set; }

    public required string Email { get; set; }

    public string NormalizedEmail { get; set; } = string.Empty;

    public ProjectRole Role { get; set; } = ProjectRole.Member;

    public InvitationStatus Status { get; set; } = InvitationStatus.Pending;

    public Guid InvitedByUserId { get; set; }

    public DateTimeOffset? AcceptedAtUtc { get; set; }
}
