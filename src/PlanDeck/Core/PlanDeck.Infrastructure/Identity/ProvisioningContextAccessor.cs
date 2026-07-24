namespace PlanDeck.Infrastructure.Identity;

public interface IProvisioningContextAccessor
{
    Guid TenantId { get; set; }
}

public sealed class ProvisioningContextAccessor : IProvisioningContextAccessor
{
    public Guid TenantId { get; set; }
}

