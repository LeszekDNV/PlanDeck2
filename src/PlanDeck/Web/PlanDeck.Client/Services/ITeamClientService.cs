using PlanDeck.Core.Shared.Contracts;

namespace PlanDeck.Client.Services;

public interface ITeamClientService
{
    Task<IReadOnlyList<TeamDto>> GetTeamsAsync();

    Task<TeamDto> CreateTeamAsync(string name, string? description);

    Task<IReadOnlyList<TeamMemberDto>> GetMembersAsync(Guid teamId);

    Task<TeamMemberDto> AddMemberAsync(Guid teamId, string email, string? displayName);

    Task<bool> RemoveMemberAsync(Guid teamId, Guid memberId);
}
