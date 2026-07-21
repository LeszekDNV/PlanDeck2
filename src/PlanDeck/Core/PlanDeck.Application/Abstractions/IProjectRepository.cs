using PlanDeck.Application.Domain;

namespace PlanDeck.Application.Abstractions;

public interface IProjectRepository
{
    Task<PlanDeckProject> CreateAsync(
        string name,
        string? description,
        string ownerEmail,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<PlanDeckProject>> ListAccessibleAsync(
        CancellationToken cancellationToken);

    Task<PlanDeckProject?> GetAsync(Guid projectId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ProjectMember>> ListMembersAsync(
        Guid projectId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ProjectTeam>> ListTeamsAsync(
        Guid projectId,
        CancellationToken cancellationToken);

    Task<ProjectMember> InviteMemberAsync(
        Guid projectId,
        string email,
        ProjectRole role,
        CancellationToken cancellationToken);

    Task RemoveMemberAsync(
        Guid projectId,
        Guid memberId,
        CancellationToken cancellationToken);

    Task<ProjectMember> ChangeMemberRoleAsync(
        Guid projectId,
        Guid memberId,
        ProjectRole role,
        CancellationToken cancellationToken);

    Task<ProjectTeam> AssignTeamAsync(
        Guid projectId,
        Guid teamId,
        CancellationToken cancellationToken);

    Task UnassignTeamAsync(
        Guid projectId,
        Guid teamId,
        CancellationToken cancellationToken);

    Task TransferOwnershipAsync(
        Guid projectId,
        Guid newOwnerMemberId,
        CancellationToken cancellationToken);

    Task EnsureCanDeleteAsync(Guid projectId, CancellationToken cancellationToken);

    Task DeleteAsync(Guid projectId, CancellationToken cancellationToken);
}
