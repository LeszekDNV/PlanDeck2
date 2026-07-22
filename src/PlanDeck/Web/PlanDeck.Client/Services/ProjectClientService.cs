using Grpc.Net.Client;
using PlanDeck.Core.Shared.Contracts;
using ProtoBuf.Grpc.Client;

namespace PlanDeck.Client.Services;

public sealed class ProjectClientService(GrpcChannel channel) : IProjectClientService
{
    public async Task<GetProjectReply> GetProjectAsync(Guid projectId)
    {
        var service = channel.CreateGrpcService<IProjectService>();
        return await service.GetProjectAsync(new GetProjectRequest { ProjectId = projectId });
    }

    public async Task<IReadOnlyList<ProjectDto>> GetProjectsAsync()
    {
        var service = channel.CreateGrpcService<IProjectService>();
        var reply = await service.ListProjectsAsync(new ListProjectsRequest());
        return reply.Projects;
    }

    public async Task<ProjectDto> CreateProjectAsync(string name, string? description)
    {
        var service = channel.CreateGrpcService<IProjectService>();
        var reply = await service.CreateProjectAsync(new CreateProjectRequest
        {
            Name = name,
            Description = description
        });
        return reply.Project;
    }

    public async Task<ProjectMemberDto> InviteMemberAsync(Guid projectId, string email, ProjectRoleDto role)
    {
        var service = channel.CreateGrpcService<IProjectService>();
        var reply = await service.InviteMemberAsync(new InviteProjectMemberRequest
        {
            ProjectId = projectId,
            Email = email,
            Role = role
        });
        return reply.Member;
    }

    public async Task<ProjectMemberDto> ChangeMemberRoleAsync(Guid projectId, Guid memberId, ProjectRoleDto role)
    {
        var service = channel.CreateGrpcService<IProjectService>();
        var reply = await service.ChangeMemberRoleAsync(new ChangeProjectMemberRoleRequest
        {
            ProjectId = projectId,
            MemberId = memberId,
            Role = role
        });
        return reply.Member;
    }

    public async Task RemoveMemberAsync(Guid projectId, Guid memberId)
    {
        var service = channel.CreateGrpcService<IProjectService>();
        await service.RemoveMemberAsync(new RemoveProjectMemberRequest
        {
            ProjectId = projectId,
            MemberId = memberId
        });
    }

    public async Task<ProjectTeamDto> AssignTeamAsync(Guid projectId, Guid teamId)
    {
        var service = channel.CreateGrpcService<IProjectService>();
        var reply = await service.AssignTeamAsync(new AssignProjectTeamRequest
        {
            ProjectId = projectId,
            TeamId = teamId
        });
        return reply.Team;
    }

    public async Task UnassignTeamAsync(Guid projectId, Guid teamId)
    {
        var service = channel.CreateGrpcService<IProjectService>();
        await service.UnassignTeamAsync(new UnassignProjectTeamRequest
        {
            ProjectId = projectId,
            TeamId = teamId
        });
    }

    public async Task TransferOwnershipAsync(Guid projectId, Guid newOwnerMemberId)
    {
        var service = channel.CreateGrpcService<IProjectService>();
        await service.TransferOwnershipAsync(new TransferProjectOwnershipRequest
        {
            ProjectId = projectId,
            NewOwnerMemberId = newOwnerMemberId
        });
    }

    public async Task DeleteProjectAsync(Guid projectId)
    {
        var service = channel.CreateGrpcService<IProjectService>();
        await service.DeleteProjectAsync(new DeleteProjectRequest { ProjectId = projectId });
    }

    public async Task<ProjectConnectionDto> ConfigureConnectionAsync(
        Guid projectId,
        string organizationUrl,
        string azureDevOpsProject,
        string estimateField,
        string descriptionField,
        string reproStepsField,
        string acceptanceCriteriaField,
        string personalAccessToken)
    {
        var service = channel.CreateGrpcService<IProjectService>();
        var reply = await service.ConfigureConnectionAsync(new ConfigureProjectConnectionRequest
        {
            ProjectId = projectId,
            OrganizationUrl = organizationUrl,
            AzureDevOpsProject = azureDevOpsProject,
            EstimateField = estimateField,
            DescriptionField = descriptionField,
            ReproStepsField = reproStepsField,
            AcceptanceCriteriaField = acceptanceCriteriaField,
            PersonalAccessToken = personalAccessToken
        });
        return reply.Connection;
    }

    public async Task<ProjectConnectionDto> UpdateConnectionAsync(
        Guid projectId,
        string organizationUrl,
        string azureDevOpsProject,
        string estimateField,
        string descriptionField,
        string reproStepsField,
        string acceptanceCriteriaField)
    {
        var service = channel.CreateGrpcService<IProjectService>();
        var reply = await service.UpdateConnectionAsync(new UpdateProjectConnectionRequest
        {
            ProjectId = projectId,
            OrganizationUrl = organizationUrl,
            AzureDevOpsProject = azureDevOpsProject,
            EstimateField = estimateField,
            DescriptionField = descriptionField,
            ReproStepsField = reproStepsField,
            AcceptanceCriteriaField = acceptanceCriteriaField
        });
        return reply.Connection;
    }

    public async Task<ProjectConnectionDto> RotateConnectionPatAsync(Guid projectId, string personalAccessToken)
    {
        var service = channel.CreateGrpcService<IProjectService>();
        var reply = await service.RotateConnectionPatAsync(new RotateProjectConnectionPatRequest
        {
            ProjectId = projectId,
            PersonalAccessToken = personalAccessToken
        });
        return reply.Connection;
    }

    public async Task<ProjectConnectionDto> SetConnectionEnabledAsync(Guid projectId, bool isEnabled)
    {
        var service = channel.CreateGrpcService<IProjectService>();
        var reply = await service.SetConnectionEnabledAsync(new SetProjectConnectionEnabledRequest
        {
            ProjectId = projectId,
            IsEnabled = isEnabled
        });
        return reply.Connection;
    }

    public async Task RemoveConnectionAsync(Guid projectId)
    {
        var service = channel.CreateGrpcService<IProjectService>();
        await service.RemoveConnectionAsync(new RemoveProjectConnectionRequest { ProjectId = projectId });
    }
}
