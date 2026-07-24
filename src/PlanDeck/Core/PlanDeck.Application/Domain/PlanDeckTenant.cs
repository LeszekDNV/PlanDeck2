namespace PlanDeck.Application.Domain;

public sealed class PlanDeckTenant
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public required string Name { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
}

