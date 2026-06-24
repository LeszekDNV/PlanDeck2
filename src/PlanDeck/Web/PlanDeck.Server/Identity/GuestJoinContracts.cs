namespace PlanDeck.Server.Identity;

public sealed record GuestJoinRequest(string? Code, string? DisplayName);

public sealed record GuestJoinResponse(Guid SessionId);
