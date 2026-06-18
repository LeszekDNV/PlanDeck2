namespace PlanDeck.Application.Abstractions;

public interface ICurrentUserContext
{
    Guid TenantId { get; }

    Guid UserId { get; }

    bool IsAuthenticated { get; }
}
