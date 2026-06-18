using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PlanDeck.Application.Abstractions;
using PlanDeck.Application.Domain;

namespace PlanDeck.Infrastructure.Persistence;

public sealed class TeamRepository(PlanDeckDbContext db, ICurrentUserContext currentUser) : ITeamRepository
{
    public async Task<Team> CreateTeamAsync(string name, string? description, CancellationToken cancellationToken)
    {
        var team = new Team
        {
            Name = name,
            Description = description,
            CreatedByUserId = currentUser.UserId
        };

        db.Teams.Add(team);
        await db.SaveChangesAsync(cancellationToken);
        return team;
    }

    public async Task<IReadOnlyList<Team>> GetTeamsAsync(CancellationToken cancellationToken)
    {
        return await db.Teams
            .AsNoTracking()
            .OrderBy(t => t.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TeamMember>> GetMembersAsync(Guid teamId, CancellationToken cancellationToken)
    {
        return await db.TeamMembers
            .AsNoTracking()
            .Where(m => m.TeamId == teamId)
            .OrderBy(m => m.Email)
            .ToListAsync(cancellationToken);
    }

    public async Task<TeamMember> AddMemberAsync(Guid teamId, string email, string? displayName, CancellationToken cancellationToken)
    {
        var teamExists = await db.Teams.AnyAsync(t => t.Id == teamId, cancellationToken);
        if (!teamExists)
        {
            throw new TeamNotFoundException(teamId);
        }

        var member = new TeamMember
        {
            TeamId = teamId,
            Email = email,
            DisplayName = displayName,
            InvitedByUserId = currentUser.UserId
        };

        db.TeamMembers.Add(member);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            throw new DuplicateTeamMemberException(teamId, email);
        }

        return member;
    }

    public async Task<bool> RemoveMemberAsync(Guid teamId, Guid memberId, CancellationToken cancellationToken)
    {
        var member = await db.TeamMembers
            .FirstOrDefaultAsync(m => m.TeamId == teamId && m.Id == memberId, cancellationToken);

        if (member is null)
        {
            return false;
        }

        db.TeamMembers.Remove(member);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
