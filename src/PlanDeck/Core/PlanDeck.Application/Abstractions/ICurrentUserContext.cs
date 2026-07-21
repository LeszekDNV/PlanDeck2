namespace PlanDeck.Application.Abstractions;

public interface ICurrentUserContext
{
    Guid TenantId { get; }

    /// <summary>
    /// Stable internal PlanDeck user ID. Guests do not have an internal user ID.
    /// </summary>
    Guid UserId { get; }

    bool IsAuthenticated { get; }

    string? DisplayName { get; }

    string? Email { get; }

    string? ParticipantId => null;

    bool IsGuest => false;

    Guid? SessionScope => null;
}
