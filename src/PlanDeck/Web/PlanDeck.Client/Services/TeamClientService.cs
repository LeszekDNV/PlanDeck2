using Grpc.Net.Client;
using PlanDeck.Core.Shared.Contracts;
using ProtoBuf.Grpc.Client;

namespace PlanDeck.Client.Services;

public sealed class TeamClientService(GrpcChannel channel) : ITeamClientService
{
    public async Task<IReadOnlyList<TeamDto>> GetTeamsAsync()
    {
        var service = channel.CreateGrpcService<ITeamService>();
        var reply = await service.ListTeamsAsync(new ListTeamsRequest());
        return reply.Teams;
    }

    public async Task<TeamDto> CreateTeamAsync(string name, string? description)
    {
        var service = channel.CreateGrpcService<ITeamService>();
        var reply = await service.CreateTeamAsync(new CreateTeamRequest
        {
            Name = name,
            Description = description
        });
        return reply.Team;
    }

    public async Task<IReadOnlyList<TeamMemberDto>> GetMembersAsync(Guid teamId)
    {
        var service = channel.CreateGrpcService<ITeamService>();
        var reply = await service.ListMembersAsync(new ListMembersRequest { TeamId = teamId });
        return reply.Members;
    }

    public async Task<TeamMemberDto> AddMemberAsync(Guid teamId, string email, string? displayName)
    {
        var service = channel.CreateGrpcService<ITeamService>();
        var reply = await service.AddMemberAsync(new AddMemberRequest
        {
            TeamId = teamId,
            Email = email,
            DisplayName = displayName
        });
        return reply.Member;
    }

    public async Task<bool> RemoveMemberAsync(Guid teamId, Guid memberId)
    {
        var service = channel.CreateGrpcService<ITeamService>();
        var reply = await service.RemoveMemberAsync(new RemoveMemberRequest
        {
            TeamId = teamId,
            MemberId = memberId
        });
        return reply.Removed;
    }
}
