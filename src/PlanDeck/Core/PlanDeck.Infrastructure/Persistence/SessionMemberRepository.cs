using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PlanDeck.Application.Abstractions;
using PlanDeck.Application.Domain;

namespace PlanDeck.Infrastructure.Persistence;

public sealed class SessionMemberRepository(PlanDeckDbContext db, ICurrentUserContext currentUser) : ISessionMemberRepository
{
    public async Task<SessionMember> AssignMemberAsync(Guid sessionId, string email, string? displayName, CancellationToken cancellationToken)
    {
        var sessionExists = await db.Sessions.AnyAsync(s => s.Id == sessionId, cancellationToken);
        if (!sessionExists)
        {
            throw new SessionNotFoundException(sessionId);
        }

        var member = new SessionMember
        {
            SessionId = sessionId,
            Email = email,
            DisplayName = displayName,
            AssignedByUserId = currentUser.UserId
        };

        db.SessionMembers.Add(member);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            throw new DuplicateSessionMemberException(sessionId, email);
        }

        return member;
    }

    public async Task<bool> RemoveMemberAsync(Guid sessionId, Guid memberId, CancellationToken cancellationToken)
    {
        var member = await db.SessionMembers
            .FirstOrDefaultAsync(m => m.SessionId == sessionId && m.Id == memberId, cancellationToken);

        if (member is null)
        {
            return false;
        }

        db.SessionMembers.Remove(member);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<SessionMember>> GetMembersAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        return await db.SessionMembers
            .AsNoTracking()
            .Where(m => m.SessionId == sessionId)
            .OrderBy(m => m.Email)
            .ToListAsync(cancellationToken);
    }
}
