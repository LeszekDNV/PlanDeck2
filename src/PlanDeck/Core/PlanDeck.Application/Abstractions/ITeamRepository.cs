using PlanDeck.Application.Domain;

namespace PlanDeck.Application.Abstractions;

public interface ITeamRepository
{
    Task<Team> CreateTeamAsync(string name, string? description, CancellationToken cancellationToken);

    Task<IReadOnlyList<Team>> GetTeamsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<TeamMember>> GetMembersAsync(Guid teamId, CancellationToken cancellationToken);

    Task<TeamMember> AddMemberAsync(Guid teamId, string email, string? displayName, CancellationToken cancellationToken);

    Task<bool> RemoveMemberAsync(Guid teamId, Guid memberId, CancellationToken cancellationToken);
}

public sealed class TeamNotFoundException(Guid teamId)
    : Exception($"Team '{teamId}' was not found in the current tenant.")
{
    public Guid TeamId { get; } = teamId;
}

public sealed class DuplicateTeamMemberException(Guid teamId, string email)
    : Exception($"A member with email '{email}' already exists in team '{teamId}'.")
{
    public Guid TeamId { get; } = teamId;

    public string Email { get; } = email;
}
