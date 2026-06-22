namespace PlanDeck.Application.Planning;

public readonly record struct RoomKey(Guid TenantId, Guid SessionId)
{
    public string GroupName => $"{TenantId}:{SessionId}";
}
