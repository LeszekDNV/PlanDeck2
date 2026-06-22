using PlanDeck.Core.Shared.Contracts;

namespace PlanDeck.Client.Services;

public interface ISessionMemberClientService
{
    Task<IReadOnlyList<SessionMemberDto>> GetMembersAsync(Guid sessionId);

    Task<SessionMemberDto> AssignMemberAsync(Guid sessionId, string email, string? displayName);

    Task<bool> RemoveMemberAsync(Guid sessionId, Guid memberId);
}
