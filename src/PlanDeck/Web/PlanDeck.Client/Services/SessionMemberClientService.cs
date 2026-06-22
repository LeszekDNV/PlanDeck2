using Grpc.Net.Client;
using PlanDeck.Core.Shared.Contracts;
using ProtoBuf.Grpc.Client;

namespace PlanDeck.Client.Services;

public sealed class SessionMemberClientService(GrpcChannel channel) : ISessionMemberClientService
{
    public async Task<IReadOnlyList<SessionMemberDto>> GetMembersAsync(Guid sessionId)
    {
        var service = channel.CreateGrpcService<ISessionMemberService>();
        var reply = await service.ListMembersAsync(new ListSessionMembersRequest { SessionId = sessionId });
        return reply.Members;
    }

    public async Task<SessionMemberDto> AssignMemberAsync(Guid sessionId, string email, string? displayName)
    {
        var service = channel.CreateGrpcService<ISessionMemberService>();
        var reply = await service.AssignMemberAsync(new AssignSessionMemberRequest
        {
            SessionId = sessionId,
            Email = email,
            DisplayName = displayName
        });
        return reply.Member;
    }

    public async Task<bool> RemoveMemberAsync(Guid sessionId, Guid memberId)
    {
        var service = channel.CreateGrpcService<ISessionMemberService>();
        var reply = await service.RemoveMemberAsync(new RemoveSessionMemberRequest
        {
            SessionId = sessionId,
            MemberId = memberId
        });
        return reply.Removed;
    }
}
