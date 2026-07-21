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
