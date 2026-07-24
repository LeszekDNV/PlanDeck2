namespace PlanDeck.Application.Abstractions;

public sealed class MemberInvitationResult<TMember>
    where TMember : class
{
    public required TMember Member { get; init; }

    public string? InvitationToken { get; init; }
}
