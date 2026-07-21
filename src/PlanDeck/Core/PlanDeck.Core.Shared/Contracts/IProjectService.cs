using System.Runtime.Serialization;
using ProtoBuf.Grpc;
using ProtoBuf.Grpc.Configuration;

namespace PlanDeck.Core.Shared.Contracts;

[Service]
public interface IProjectService
{
    [Operation]
    Task<CreateProjectReply> CreateProjectAsync(
        CreateProjectRequest request,
        CallContext context = default);

    [Operation]
    Task<ListProjectsReply> ListProjectsAsync(
        ListProjectsRequest request,
        CallContext context = default);

    [Operation]
    Task<GetProjectReply> GetProjectAsync(
        GetProjectRequest request,
        CallContext context = default);

    [Operation]
    Task<ProjectMemberReply> InviteMemberAsync(
        InviteProjectMemberRequest request,
        CallContext context = default);

    [Operation]
    Task<ProjectMemberReply> ChangeMemberRoleAsync(
        ChangeProjectMemberRoleRequest request,
        CallContext context = default);

    [Operation]
    Task<EmptyProjectReply> RemoveMemberAsync(
        RemoveProjectMemberRequest request,
        CallContext context = default);

    [Operation]
    Task<ProjectTeamReply> AssignTeamAsync(
        AssignProjectTeamRequest request,
        CallContext context = default);

    [Operation]
    Task<EmptyProjectReply> UnassignTeamAsync(
        UnassignProjectTeamRequest request,
        CallContext context = default);

    [Operation]
    Task<EmptyProjectReply> TransferOwnershipAsync(
        TransferProjectOwnershipRequest request,
        CallContext context = default);

    [Operation]
    Task<EmptyProjectReply> DeleteProjectAsync(
        DeleteProjectRequest request,
        CallContext context = default);

    [Operation]
    Task<ProjectConnectionReply> ConfigureConnectionAsync(
        ConfigureProjectConnectionRequest request,
        CallContext context = default);

    [Operation]
    Task<ProjectConnectionReply> UpdateConnectionAsync(
        UpdateProjectConnectionRequest request,
        CallContext context = default);

    [Operation]
    Task<ProjectConnectionReply> RotateConnectionPatAsync(
        RotateProjectConnectionPatRequest request,
        CallContext context = default);

    [Operation]
    Task<ProjectConnectionReply> SetConnectionEnabledAsync(
        SetProjectConnectionEnabledRequest request,
        CallContext context = default);

    [Operation]
    Task<EmptyProjectReply> RemoveConnectionAsync(
        RemoveProjectConnectionRequest request,
        CallContext context = default);
}

[DataContract]
public enum ProjectRoleDto
{
    [EnumMember] Member = 1,
    [EnumMember] Admin = 2,
    [EnumMember] Owner = 3
}

[DataContract]
public enum InvitationStatusDto
{
    [EnumMember] Pending = 0,
    [EnumMember] Accepted = 1
}

[DataContract]
public enum ProjectMembershipSourceDto
{
    [EnumMember] Direct = 1,
    [EnumMember] Team = 2
}

[DataContract]
public enum ProjectConnectionValidationStateDto
{
    [EnumMember] NotValidated = 0,
    [EnumMember] Valid = 1,
    [EnumMember] Invalid = 2
}

[DataContract]
public sealed class ProjectDto
{
    [DataMember(Order = 1)]
    public Guid Id { get; set; }

    [DataMember(Order = 2)]
    public string Name { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string? Description { get; set; }

    [DataMember(Order = 4)]
    public ProjectRoleDto EffectiveRole { get; set; }

    [DataMember(Order = 5)]
    public ProjectMembershipSourceDto MembershipSource { get; set; }
}

[DataContract]
public sealed class ProjectMemberDto
{
    [DataMember(Order = 1)]
    public Guid Id { get; set; }

    [DataMember(Order = 2)]
    public string Email { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public ProjectRoleDto Role { get; set; }

    [DataMember(Order = 4)]
    public InvitationStatusDto Status { get; set; }
}

[DataContract]
public sealed class ProjectTeamDto
{
    [DataMember(Order = 1)]
    public Guid Id { get; set; }

    [DataMember(Order = 2)]
    public Guid TeamId { get; set; }
}

[DataContract]
public sealed class CreateProjectRequest
{
    [DataMember(Order = 1)]
    public string Name { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string? Description { get; set; }
}

[DataContract]
public sealed class CreateProjectReply
{
    [DataMember(Order = 1)]
    public ProjectDto Project { get; set; } = new();
}

[DataContract]
public sealed class ListProjectsRequest;

[DataContract]
public sealed class ListProjectsReply
{
    [DataMember(Order = 1)]
    public List<ProjectDto> Projects { get; set; } = [];
}

[DataContract]
public sealed class GetProjectRequest
{
    [DataMember(Order = 1)]
    public Guid ProjectId { get; set; }
}

[DataContract]
public sealed class GetProjectReply
{
    [DataMember(Order = 1)]
    public ProjectDto Project { get; set; } = new();

    [DataMember(Order = 2)]
    public List<ProjectMemberDto> Members { get; set; } = [];

    [DataMember(Order = 3)]
    public List<ProjectTeamDto> Teams { get; set; } = [];

    [DataMember(Order = 4)]
    public ProjectConnectionDto? Connection { get; set; }
}

[DataContract]
public sealed class InviteProjectMemberRequest
{
    [DataMember(Order = 1)]
    public Guid ProjectId { get; set; }

    [DataMember(Order = 2)]
    public string Email { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public ProjectRoleDto Role { get; set; }
}

[DataContract]
public sealed class ChangeProjectMemberRoleRequest
{
    [DataMember(Order = 1)]
    public Guid ProjectId { get; set; }

    [DataMember(Order = 2)]
    public Guid MemberId { get; set; }

    [DataMember(Order = 3)]
    public ProjectRoleDto Role { get; set; }
}

[DataContract]
public sealed class RemoveProjectMemberRequest
{
    [DataMember(Order = 1)]
    public Guid ProjectId { get; set; }

    [DataMember(Order = 2)]
    public Guid MemberId { get; set; }
}

[DataContract]
public sealed class ProjectMemberReply
{
    [DataMember(Order = 1)]
    public ProjectMemberDto Member { get; set; } = new();
}

[DataContract]
public sealed class AssignProjectTeamRequest
{
    [DataMember(Order = 1)]
    public Guid ProjectId { get; set; }

    [DataMember(Order = 2)]
    public Guid TeamId { get; set; }
}

[DataContract]
public sealed class UnassignProjectTeamRequest
{
    [DataMember(Order = 1)]
    public Guid ProjectId { get; set; }

    [DataMember(Order = 2)]
    public Guid TeamId { get; set; }
}

[DataContract]
public sealed class ProjectTeamReply
{
    [DataMember(Order = 1)]
    public ProjectTeamDto Team { get; set; } = new();
}

[DataContract]
public sealed class TransferProjectOwnershipRequest
{
    [DataMember(Order = 1)]
    public Guid ProjectId { get; set; }

    [DataMember(Order = 2)]
    public Guid NewOwnerMemberId { get; set; }
}

[DataContract]
public sealed class DeleteProjectRequest
{
    [DataMember(Order = 1)]
    public Guid ProjectId { get; set; }
}

[DataContract]
public sealed class EmptyProjectReply;

[DataContract]
public sealed class ProjectConnectionDto
{
    [DataMember(Order = 1)]
    public bool IsEnabled { get; set; }

    [DataMember(Order = 2)]
    public ProjectConnectionValidationStateDto ValidationState { get; set; }

    [DataMember(Order = 3)]
    public DateTimeOffset? LastValidatedAtUtc { get; set; }
}

[DataContract]
public sealed class ProjectConnectionReply
{
    [DataMember(Order = 1)]
    public ProjectConnectionDto Connection { get; set; } = new();
}

[DataContract]
public sealed class ConfigureProjectConnectionRequest
{
    [DataMember(Order = 1)]
    public Guid ProjectId { get; set; }

    [DataMember(Order = 2)]
    public string OrganizationUrl { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string AzureDevOpsProject { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string EstimateField { get; set; } = "Microsoft.VSTS.Scheduling.StoryPoints";

    [DataMember(Order = 5)]
    public string DescriptionField { get; set; } = "System.Description";

    [DataMember(Order = 6)]
    public string ReproStepsField { get; set; } = "Microsoft.VSTS.TCM.ReproSteps";

    [DataMember(Order = 7)]
    public string AcceptanceCriteriaField { get; set; } =
        "Microsoft.VSTS.Common.AcceptanceCriteria";

    [DataMember(Order = 8)]
    public string PersonalAccessToken { get; set; } = string.Empty;
}

[DataContract]
public sealed class UpdateProjectConnectionRequest
{
    [DataMember(Order = 1)]
    public Guid ProjectId { get; set; }

    [DataMember(Order = 2)]
    public string OrganizationUrl { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string AzureDevOpsProject { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string EstimateField { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public string DescriptionField { get; set; } = string.Empty;

    [DataMember(Order = 6)]
    public string ReproStepsField { get; set; } = string.Empty;

    [DataMember(Order = 7)]
    public string AcceptanceCriteriaField { get; set; } = string.Empty;
}

[DataContract]
public sealed class RotateProjectConnectionPatRequest
{
    [DataMember(Order = 1)]
    public Guid ProjectId { get; set; }

    [DataMember(Order = 2)]
    public string PersonalAccessToken { get; set; } = string.Empty;
}

[DataContract]
public sealed class SetProjectConnectionEnabledRequest
{
    [DataMember(Order = 1)]
    public Guid ProjectId { get; set; }

    [DataMember(Order = 2)]
    public bool IsEnabled { get; set; }
}

[DataContract]
public sealed class RemoveProjectConnectionRequest
{
    [DataMember(Order = 1)]
    public Guid ProjectId { get; set; }
}
