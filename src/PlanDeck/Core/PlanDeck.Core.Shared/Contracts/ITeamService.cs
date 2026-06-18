using System.Runtime.Serialization;
using ProtoBuf.Grpc;
using ProtoBuf.Grpc.Configuration;

namespace PlanDeck.Core.Shared.Contracts;

[Service]
public interface ITeamService
{
    [Operation]
    Task<CreateTeamReply> CreateTeamAsync(CreateTeamRequest request, CallContext context = default);

    [Operation]
    Task<ListTeamsReply> ListTeamsAsync(ListTeamsRequest request, CallContext context = default);

    [Operation]
    Task<AddMemberReply> AddMemberAsync(AddMemberRequest request, CallContext context = default);

    [Operation]
    Task<RemoveMemberReply> RemoveMemberAsync(RemoveMemberRequest request, CallContext context = default);

    [Operation]
    Task<ListMembersReply> ListMembersAsync(ListMembersRequest request, CallContext context = default);
}

[DataContract]
public sealed class TeamDto
{
    [DataMember(Order = 1)]
    public Guid Id { get; set; }

    [DataMember(Order = 2)]
    public string Name { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string? Description { get; set; }

    [DataMember(Order = 4)]
    public DateTime CreatedAtUtc { get; set; }
}

[DataContract]
public sealed class TeamMemberDto
{
    [DataMember(Order = 1)]
    public Guid Id { get; set; }

    [DataMember(Order = 2)]
    public Guid TeamId { get; set; }

    [DataMember(Order = 3)]
    public string Email { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string? DisplayName { get; set; }
}

[DataContract]
public sealed class CreateTeamRequest
{
    [DataMember(Order = 1)]
    public string Name { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string? Description { get; set; }
}

[DataContract]
public sealed class CreateTeamReply
{
    [DataMember(Order = 1)]
    public TeamDto Team { get; set; } = new();
}

[DataContract]
public sealed class ListTeamsRequest
{
}

[DataContract]
public sealed class ListTeamsReply
{
    [DataMember(Order = 1)]
    public List<TeamDto> Teams { get; set; } = [];
}

[DataContract]
public sealed class AddMemberRequest
{
    [DataMember(Order = 1)]
    public Guid TeamId { get; set; }

    [DataMember(Order = 2)]
    public string Email { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string? DisplayName { get; set; }
}

[DataContract]
public sealed class AddMemberReply
{
    [DataMember(Order = 1)]
    public TeamMemberDto Member { get; set; } = new();
}

[DataContract]
public sealed class RemoveMemberRequest
{
    [DataMember(Order = 1)]
    public Guid TeamId { get; set; }

    [DataMember(Order = 2)]
    public Guid MemberId { get; set; }
}

[DataContract]
public sealed class RemoveMemberReply
{
    [DataMember(Order = 1)]
    public bool Removed { get; set; }
}

[DataContract]
public sealed class ListMembersRequest
{
    [DataMember(Order = 1)]
    public Guid TeamId { get; set; }
}

[DataContract]
public sealed class ListMembersReply
{
    [DataMember(Order = 1)]
    public List<TeamMemberDto> Members { get; set; } = [];
}
