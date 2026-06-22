using Grpc.Core;
using PlanDeck.Application.Abstractions;
using PlanDeck.Application.Domain;
using PlanDeck.Core.Shared.Contracts;
using PlanDeck.Core.Shared.Validation;
using ProtoBuf.Grpc;

namespace PlanDeck.Application.Services;

public sealed class SessionMemberGrpcService(ISessionMemberRepository repository) : ISessionMemberService
{
    public async Task<AssignSessionMemberReply> AssignMemberAsync(AssignSessionMemberRequest request, CallContext context = default)
    {
        if (request.SessionId == Guid.Empty)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, SessionMemberValidationMessages.SessionIdRequired));
        }

        var email = request.Email?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(email) || !email.Contains('@'))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, SessionMemberValidationMessages.EmailRequired));
        }

        try
        {
            var member = await repository.AssignMemberAsync(
                request.SessionId,
                email,
                string.IsNullOrWhiteSpace(request.DisplayName) ? null : request.DisplayName.Trim(),
                context.CancellationToken);

            return new AssignSessionMemberReply { Member = ToDto(member) };
        }
        catch (SessionNotFoundException ex)
        {
            throw new RpcException(new Status(StatusCode.NotFound, ex.Message));
        }
        catch (DuplicateSessionMemberException ex)
        {
            throw new RpcException(new Status(StatusCode.AlreadyExists, ex.Message));
        }
    }

    public async Task<RemoveSessionMemberReply> RemoveMemberAsync(RemoveSessionMemberRequest request, CallContext context = default)
    {
        if (request.SessionId == Guid.Empty || request.MemberId == Guid.Empty)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, SessionMemberValidationMessages.SessionIdRequired));
        }

        var removed = await repository.RemoveMemberAsync(request.SessionId, request.MemberId, context.CancellationToken);
        return new RemoveSessionMemberReply { Removed = removed };
    }

    public async Task<ListSessionMembersReply> ListMembersAsync(ListSessionMembersRequest request, CallContext context = default)
    {
        if (request.SessionId == Guid.Empty)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, SessionMemberValidationMessages.SessionIdRequired));
        }

        var members = await repository.GetMembersAsync(request.SessionId, context.CancellationToken);
        return new ListSessionMembersReply { Members = members.Select(ToDto).ToList() };
    }

    private static SessionMemberDto ToDto(SessionMember member) => new()
    {
        Id = member.Id,
        SessionId = member.SessionId,
        Email = member.Email,
        DisplayName = member.DisplayName
    };
}
