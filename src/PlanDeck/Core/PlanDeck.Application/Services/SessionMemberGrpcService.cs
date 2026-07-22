using Grpc.Core;
using PlanDeck.Application.Abstractions;
using PlanDeck.Application.Domain;
using PlanDeck.Core.Shared.Contracts;
using PlanDeck.Core.Shared.Validation;
using ProtoBuf.Grpc;

namespace PlanDeck.Application.Services;

public sealed class SessionMemberGrpcService(
    ISessionMemberRepository repository,
    ISessionAccessResolver sessionAccessResolver,
    ICurrentUserContext currentUser) : ISessionMemberService
{
    public async Task<AssignSessionMemberReply> AssignMemberAsync(AssignSessionMemberRequest request, CallContext context = default)
    {
        GuestAccessGuard.RejectGuests(currentUser);

        if (request.SessionId == Guid.Empty)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, SessionMemberValidationMessages.SessionIdRequired));
        }

        var email = request.Email?.Trim() ?? string.Empty;
        if (!EmailValidator.IsValid(email))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, SessionMemberValidationMessages.EmailRequired));
        }

        try
        {
            await RequireSessionRoleAsync(request.SessionId, ProjectRole.Admin, context.CancellationToken);

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
        GuestAccessGuard.RejectGuests(currentUser);

        if (request.SessionId == Guid.Empty || request.MemberId == Guid.Empty)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, SessionMemberValidationMessages.SessionIdRequired));
        }

        await RequireSessionRoleAsync(request.SessionId, ProjectRole.Admin, context.CancellationToken);
        var removed = await repository.RemoveMemberAsync(request.SessionId, request.MemberId, context.CancellationToken);
        return new RemoveSessionMemberReply { Removed = removed };
    }

    public async Task<ListSessionMembersReply> ListMembersAsync(ListSessionMembersRequest request, CallContext context = default)
    {
        GuestAccessGuard.RejectGuests(currentUser);

        if (request.SessionId == Guid.Empty)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, SessionMemberValidationMessages.SessionIdRequired));
        }

        await RequireSessionRoleAsync(request.SessionId, ProjectRole.Member, context.CancellationToken);
        var members = await repository.GetMembersAsync(request.SessionId, context.CancellationToken);
        return new ListSessionMembersReply { Members = members.Select(ToDto).ToList() };
    }

    private async Task RequireSessionRoleAsync(Guid sessionId, ProjectRole minimumRole, CancellationToken cancellationToken)
    {
        var access = await sessionAccessResolver.ResolveProjectAccessAsync(sessionId, cancellationToken);
        if (access is null)
        {
            throw new RpcException(new Status(StatusCode.NotFound, $"Session '{sessionId}' was not found."));
        }

        if (access.Value.Role < minimumRole)
        {
            throw new RpcException(new Status(StatusCode.PermissionDenied, $"Session '{sessionId}' requires the '{minimumRole}' role."));
        }
    }

    private static SessionMemberDto ToDto(SessionMember member) => new()
    {
        Id = member.Id,
        SessionId = member.SessionId,
        Email = member.Email,
        DisplayName = member.DisplayName
    };
}
