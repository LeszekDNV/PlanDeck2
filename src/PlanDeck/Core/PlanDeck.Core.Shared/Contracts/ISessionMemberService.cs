using System.Runtime.Serialization;
using ProtoBuf.Grpc;
using ProtoBuf.Grpc.Configuration;

namespace PlanDeck.Core.Shared.Contracts;

[Service]
public interface ISessionMemberService
{
    [Operation]
    Task<AssignSessionMemberReply> AssignMemberAsync(AssignSessionMemberRequest request, CallContext context = default);

    [Operation]
    Task<RemoveSessionMemberReply> RemoveMemberAsync(RemoveSessionMemberRequest request, CallContext context = default);

    [Operation]
    Task<ListSessionMembersReply> ListMembersAsync(ListSessionMembersRequest request, CallContext context = default);
}

[DataContract]
public sealed class SessionMemberDto
{
    [DataMember(Order = 1)]
    public Guid Id { get; set; }

    [DataMember(Order = 2)]
    public Guid SessionId { get; set; }

    [DataMember(Order = 3)]
    public string Email { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string? DisplayName { get; set; }
}

[DataContract]
public sealed class AssignSessionMemberRequest
{
    [DataMember(Order = 1)]
    public Guid SessionId { get; set; }

    [DataMember(Order = 2)]
    public string Email { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string? DisplayName { get; set; }
}

[DataContract]
public sealed class AssignSessionMemberReply
{
    [DataMember(Order = 1)]
    public SessionMemberDto Member { get; set; } = new();
}

[DataContract]
public sealed class RemoveSessionMemberRequest
{
    [DataMember(Order = 1)]
    public Guid SessionId { get; set; }

    [DataMember(Order = 2)]
    public Guid MemberId { get; set; }
}

[DataContract]
public sealed class RemoveSessionMemberReply
{
    [DataMember(Order = 1)]
    public bool Removed { get; set; }
}

[DataContract]
public sealed class ListSessionMembersRequest
{
    [DataMember(Order = 1)]
    public Guid SessionId { get; set; }
}

[DataContract]
public sealed class ListSessionMembersReply
{
    [DataMember(Order = 1)]
    public List<SessionMemberDto> Members { get; set; } = [];
}
