using Grpc.Core;
using PlanDeck.Application.Abstractions;
using PlanDeck.Application.Domain;
using PlanDeck.Core.Shared.Contracts;
using PlanDeck.Core.Shared.Validation;
using ProtoBuf.Grpc;

namespace PlanDeck.Application.Services;

public sealed class TeamGrpcService(ITeamRepository repository, ICurrentUserContext currentUser) : ITeamService
{
    public async Task<CreateTeamReply> CreateTeamAsync(CreateTeamRequest request, CallContext context = default)
    {
        GuestAccessGuard.RejectGuests(currentUser);

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Team name is required."));
        }

        var team = await repository.CreateTeamAsync(
            request.Name.Trim(),
            string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            context.CancellationToken);

        return new CreateTeamReply { Team = ToDto(team) };
    }

    public async Task<ListTeamsReply> ListTeamsAsync(ListTeamsRequest request, CallContext context = default)
    {
        GuestAccessGuard.RejectGuests(currentUser);

        var teams = await repository.GetTeamsAsync(context.CancellationToken);
        return new ListTeamsReply { Teams = teams.Select(ToDto).ToList() };
    }

    public async Task<AddMemberReply> AddMemberAsync(AddMemberRequest request, CallContext context = default)
    {
        GuestAccessGuard.RejectGuests(currentUser);

        if (request.TeamId == Guid.Empty)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "TeamId is required."));
        }

        var email = request.Email?.Trim() ?? string.Empty;
        if (!EmailValidator.IsValid(email))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "A valid member email is required."));
        }

        try
        {
            var result = await repository.AddMemberAsync(
                request.TeamId,
                email,
                string.IsNullOrWhiteSpace(request.DisplayName) ? null : request.DisplayName.Trim(),
                context.CancellationToken);

            return new AddMemberReply { Member = ToDto(result.Member, result.InvitationToken) };
        }
        catch (TeamNotFoundException ex)
        {
            throw new RpcException(new Status(StatusCode.NotFound, ex.Message));
        }
        catch (DuplicateTeamMemberException ex)
        {
            throw new RpcException(new Status(StatusCode.AlreadyExists, ex.Message));
        }
        catch (AccountTenantConflictException ex)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, ex.Message));
        }
    }

    public async Task<RemoveMemberReply> RemoveMemberAsync(RemoveMemberRequest request, CallContext context = default)
    {
        GuestAccessGuard.RejectGuests(currentUser);

        if (request.TeamId == Guid.Empty || request.MemberId == Guid.Empty)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "TeamId and MemberId are required."));
        }

        var removed = await repository.RemoveMemberAsync(request.TeamId, request.MemberId, context.CancellationToken);
        return new RemoveMemberReply { Removed = removed };
    }

    public async Task<ListMembersReply> ListMembersAsync(ListMembersRequest request, CallContext context = default)
    {
        GuestAccessGuard.RejectGuests(currentUser);

        if (request.TeamId == Guid.Empty)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "TeamId is required."));
        }

        var members = await repository.GetMembersAsync(request.TeamId, context.CancellationToken);
        return new ListMembersReply { Members = members.Select(member => ToDto(member)).ToList() };
    }

    public async Task<DeleteTeamReply> DeleteTeamAsync(DeleteTeamRequest request, CallContext context = default)
    {
        GuestAccessGuard.RejectGuests(currentUser);

        if (request.TeamId == Guid.Empty)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "TeamId is required."));
        }

        var result = await repository.DeleteTeamAsync(request.TeamId, context.CancellationToken);
        return result switch
        {
            DeleteTeamResult.Deleted => new DeleteTeamReply { Deleted = true },
            DeleteTeamResult.NotFound => throw new RpcException(new Status(StatusCode.NotFound, "Team was not found.")),
            DeleteTeamResult.Forbidden => throw new RpcException(new Status(StatusCode.PermissionDenied, "Only the team creator can delete the team.")),
            _ => throw new RpcException(new Status(StatusCode.Internal, "The team deletion operation could not be completed."))
        };
    }

    private static TeamDto ToDto(Team team) => new()
    {
        Id = team.Id,
        Name = team.Name,
        Description = team.Description,
        CreatedAtUtc = team.CreatedAtUtc.UtcDateTime
    };

    private static TeamMemberDto ToDto(TeamMember member, string? invitationToken = null) => new()
    {
        Id = member.Id,
        TeamId = member.TeamId,
        Email = member.Email,
        DisplayName = member.DisplayName,
        InvitationToken = invitationToken
    };
}
