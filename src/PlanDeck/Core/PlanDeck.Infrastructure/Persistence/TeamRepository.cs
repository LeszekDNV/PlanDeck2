using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PlanDeck.Application.Abstractions;
using PlanDeck.Application.Domain;

namespace PlanDeck.Infrastructure.Persistence;

public sealed class TeamRepository(
    PlanDeckDbContext db,
    ICurrentUserContext currentUser,
    IIdentityAccountRepository identityAccountRepository) : ITeamRepository
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

    public async Task<MemberInvitationResult<TeamMember>> AddMemberAsync(
        Guid teamId,
        string email,
        string? displayName,
        CancellationToken cancellationToken)
    {
        var teamExists = await db.Teams.AnyAsync(t => t.Id == teamId, cancellationToken);
        if (!teamExists)
        {
            throw new TeamNotFoundException(teamId);
        }

        var normalizedEmail = email.Trim().ToUpperInvariant();
        var identityAccount = await identityAccountRepository.FindByNormalizedEmailAsync(
            normalizedEmail,
            cancellationToken);

        Guid? appUserId = null;
        DateTimeOffset? acceptedAtUtc = null;
        string? invitationToken = null;

        if (identityAccount is { EmailConfirmed: true })
        {
            var appUser = await db.AppUsers.AsNoTracking()
                .IgnoreQueryFilters()
                .SingleOrDefaultAsync(
                    user => user.Id == identityAccount.Id && user.IsActive,
                    cancellationToken);

            if (appUser is not null)
            {
                if (appUser.TenantId != currentUser.TenantId)
                {
                    throw new AccountTenantConflictException(email);
                }

                appUserId = appUser.Id;
                acceptedAtUtc = DateTimeOffset.UtcNow;
            }
        }

        var member = new TeamMember
        {
            TeamId = teamId,
            Email = email,
            DisplayName = displayName,
            AppUserId = appUserId,
            Status = appUserId is null ? InvitationStatus.Pending : InvitationStatus.Accepted,
            InvitedByUserId = currentUser.UserId,
            AcceptedAtUtc = acceptedAtUtc
        };

        db.TeamMembers.Add(member);

        if (appUserId is null)
        {
            invitationToken = GenerateInvitationToken();
            db.TenantInvitations.Add(new TenantInvitation
            {
                TokenHash = HashToken(invitationToken),
                NormalizedEmail = normalizedEmail,
                Role = TenantRole.Member,
                ExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(7)
            });
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            throw new DuplicateTeamMemberException(teamId, email);
        }

        return new MemberInvitationResult<TeamMember>
        {
            Member = member,
            InvitationToken = invitationToken
        };
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

    public async Task<int> ActivatePendingMembershipsByEmailAsync(
        Guid tenantId,
        Guid appUserId,
        string normalizedEmail,
        DateTimeOffset acceptedAtUtc,
        CancellationToken cancellationToken)
    {
        var pending = await db.TeamMembers
            .IgnoreQueryFilters()
            .Where(m =>
                m.TenantId == tenantId
                && m.NormalizedEmail == normalizedEmail
                && m.Status == InvitationStatus.Pending)
            .ToListAsync(cancellationToken);

        foreach (var member in pending)
        {
            member.AppUserId = appUserId;
            member.Status = InvitationStatus.Accepted;
            member.AcceptedAtUtc = acceptedAtUtc;
        }

        if (pending.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return pending.Count;
    }

    public async Task<DeleteTeamResult> DeleteTeamAsync(
        Guid teamId,
        CancellationToken cancellationToken)
    {
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(
            async (token) =>
            {
                await using var transaction = await BeginTransactionAsync(token);

                var team = await db.Teams
                    .FirstOrDefaultAsync(t => t.Id == teamId, token);

                if (team is null)
                {
                    return DeleteTeamResult.NotFound;
                }

                if (team.CreatedByUserId != currentUser.UserId)
                {
                    return DeleteTeamResult.Forbidden;
                }

                var members = await db.TeamMembers
                    .Where(m => m.TeamId == teamId)
                    .ToListAsync(token);
                db.TeamMembers.RemoveRange(members);

                db.Teams.Remove(team);
                await db.SaveChangesAsync(token);

                if (transaction is not null)
                {
                    await transaction.CommitAsync(token);
                }

                return DeleteTeamResult.Deleted;
            },
            cancellationToken);
    }

    private static string GenerateInvitationToken() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    private static byte[] HashToken(string token) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(token));

    private async Task<IDbContextTransaction?> BeginTransactionAsync(
        CancellationToken cancellationToken) =>
        db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;
}
