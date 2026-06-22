using PlanDeck.Application.Domain;

namespace PlanDeck.Application.Abstractions;

public interface ISessionMemberRepository
{
    Task<SessionMember> AssignMemberAsync(Guid sessionId, string email, string? displayName, CancellationToken cancellationToken);

    Task<bool> RemoveMemberAsync(Guid sessionId, Guid memberId, CancellationToken cancellationToken);

    Task<IReadOnlyList<SessionMember>> GetMembersAsync(Guid sessionId, CancellationToken cancellationToken);
}

public sealed class DuplicateSessionMemberException(Guid sessionId, string email)
    : Exception($"A member with email '{email}' is already assigned to session '{sessionId}'.")
{
    public Guid SessionId { get; } = sessionId;

    public string Email { get; } = email;
}
