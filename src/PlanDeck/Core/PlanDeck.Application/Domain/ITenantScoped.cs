namespace PlanDeck.Application.Domain;

public interface ITenantScoped
{
    Guid TenantId { get; set; }
}
