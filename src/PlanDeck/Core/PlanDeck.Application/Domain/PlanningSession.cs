namespace PlanDeck.Application.Domain;

public sealed class PlanningSession : TenantEntity
{
    public required string Name { get; set; }

    public Guid? TeamId { get; set; }

    public Guid CreatedByUserId { get; set; }

    public SessionStatus Status { get; set; } = SessionStatus.Draft;

    public VotingScaleType ScaleType { get; set; } = VotingScaleType.Fibonacci;

    public List<string> ScaleValues { get; set; } = [];

    public string? ShareCode { get; set; }

    public List<SessionTask> Tasks { get; set; } = [];
}
