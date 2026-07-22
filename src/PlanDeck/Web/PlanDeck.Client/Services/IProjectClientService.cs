using PlanDeck.Core.Shared.Contracts;

namespace PlanDeck.Client.Services;

public interface IProjectClientService
{
    Task<IReadOnlyList<ProjectDto>> GetProjectsAsync();

    Task<GetProjectReply> GetProjectAsync(Guid projectId);

    Task<ProjectDto> CreateProjectAsync(string name, string? description);

    Task<ProjectMemberDto> InviteMemberAsync(Guid projectId, string email, ProjectRoleDto role);

    Task<ProjectMemberDto> ChangeMemberRoleAsync(Guid projectId, Guid memberId, ProjectRoleDto role);

    Task RemoveMemberAsync(Guid projectId, Guid memberId);

    Task<ProjectTeamDto> AssignTeamAsync(Guid projectId, Guid teamId);

    Task UnassignTeamAsync(Guid projectId, Guid teamId);

    Task TransferOwnershipAsync(Guid projectId, Guid newOwnerMemberId);

    Task DeleteProjectAsync(Guid projectId);

    Task<ProjectConnectionDto> ConfigureConnectionAsync(
        Guid projectId,
        string organizationUrl,
        string azureDevOpsProject,
        string estimateField,
        string descriptionField,
        string reproStepsField,
        string acceptanceCriteriaField,
        string personalAccessToken);

    Task<ProjectConnectionDto> UpdateConnectionAsync(
        Guid projectId,
        string organizationUrl,
        string azureDevOpsProject,
        string estimateField,
        string descriptionField,
        string reproStepsField,
        string acceptanceCriteriaField);

    Task<ProjectConnectionDto> RotateConnectionPatAsync(Guid projectId, string personalAccessToken);

    Task<ProjectConnectionDto> SetConnectionEnabledAsync(Guid projectId, bool isEnabled);

    Task RemoveConnectionAsync(Guid projectId);
}
