using Microsoft.EntityFrameworkCore;
using PlanDeck.Application.Abstractions;
using PlanDeck.Application.Domain;

namespace PlanDeck.Infrastructure.Persistence;

public sealed class SessionRepository(PlanDeckDbContext db, ICurrentUserContext currentUser) : ISessionRepository
{
    public async Task<PlanningSession> CreateSessionAsync(PlanningSession session, CancellationToken cancellationToken)
    {
        session.CreatedByUserId = currentUser.UserId;

        db.Sessions.Add(session);
        await db.SaveChangesAsync(cancellationToken);
        return session;
    }

    public async Task<IReadOnlyList<PlanningSession>> GetSessionsAsync(CancellationToken cancellationToken)
    {
        return await db.Sessions
            .AsNoTracking()
            .Include(s => s.Tasks)
            .OrderByDescending(s => s.CreatedAtUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task<PlanningSession?> GetSessionAsync(Guid id, CancellationToken cancellationToken)
    {
        return await db.Sessions
            .Include(s => s.Tasks)
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
    }

    public async Task<PlanningSession> UpdateSessionAsync(PlanningSession session, CancellationToken cancellationToken)
    {
        await db.SaveChangesAsync(cancellationToken);
        return session;
    }

    public async Task<bool> DeleteSessionAsync(Guid id, CancellationToken cancellationToken)
    {
        var session = await db.Sessions.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (session is null)
        {
            return false;
        }

        db.Sessions.Remove(session);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> SetAgreedEstimateAsync(Guid sessionId, Guid taskId, string? estimate, CancellationToken cancellationToken)
    {
        var task = await db.SessionTasks
            .FirstOrDefaultAsync(t => t.Id == taskId && t.SessionId == sessionId, cancellationToken);
        if (task is null)
        {
            return false;
        }

        task.AgreedEstimate = estimate;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
