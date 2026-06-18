using Grpc.Core;
using PlanDeck.Application.Abstractions;
using PlanDeck.Application.Domain;
using PlanDeck.Core.Shared.Contracts;
using ProtoBuf.Grpc;

namespace PlanDeck.Application.Services;

public sealed class TeamGrpcService(ITeamRepository repository) : ITeamService
{
    public async Task<CreateTeamReply> CreateTeamAsync(CreateTeamRequest request, CallContext context = default)
    {
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
        var teams = await repository.GetTeamsAsync(context.CancellationToken);
        return new ListTeamsReply { Teams = teams.Select(ToDto).ToList() };
    }

    public async Task<AddMemberReply> AddMemberAsync(AddMemberRequest request, CallContext context = default)
    {
        if (request.TeamId == Guid.Empty)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "TeamId is required."));
        }

        var email = request.Email?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(email) || !email.Contains('@'))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "A valid member email is required."));
        }

        try
        {
            var member = await repository.AddMemberAsync(
                request.TeamId,
                email,
                string.IsNullOrWhiteSpace(request.DisplayName) ? null : request.DisplayName.Trim(),
                context.CancellationToken);

            return new AddMemberReply { Member = ToDto(member) };
        }
        catch (TeamNotFoundException ex)
        {
            throw new RpcException(new Status(StatusCode.NotFound, ex.Message));
        }
        catch (DuplicateTeamMemberException ex)
        {
            throw new RpcException(new Status(StatusCode.AlreadyExists, ex.Message));
        }
    }

    public async Task<RemoveMemberReply> RemoveMemberAsync(RemoveMemberRequest request, CallContext context = default)
    {
        if (request.TeamId == Guid.Empty || request.MemberId == Guid.Empty)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "TeamId and MemberId are required."));
        }

        var removed = await repository.RemoveMemberAsync(request.TeamId, request.MemberId, context.CancellationToken);
        return new RemoveMemberReply { Removed = removed };
    }

    public async Task<ListMembersReply> ListMembersAsync(ListMembersRequest request, CallContext context = default)
    {
        if (request.TeamId == Guid.Empty)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "TeamId is required."));
        }

        var members = await repository.GetMembersAsync(request.TeamId, context.CancellationToken);
        return new ListMembersReply { Members = members.Select(ToDto).ToList() };
    }

    private static TeamDto ToDto(Team team) => new()
    {
        Id = team.Id,
        Name = team.Name,
        Description = team.Description,
        CreatedAtUtc = team.CreatedAtUtc
    };

    private static TeamMemberDto ToDto(TeamMember member) => new()
    {
        Id = member.Id,
        TeamId = member.TeamId,
        Email = member.Email,
        DisplayName = member.DisplayName
    };
}
