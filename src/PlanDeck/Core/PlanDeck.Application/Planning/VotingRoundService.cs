using PlanDeck.Application.Abstractions;
using PlanDeck.Application.Domain;

namespace PlanDeck.Application.Planning;

public sealed class VotingRoundService(
    ISessionRepository sessionRepository,
    ISessionMemberRepository sessionMemberRepository) : IVotingRoundService
{
    public async Task<bool> IsAuthorizedParticipantAsync(Guid sessionId, Guid userId, string? email, CancellationToken cancellationToken)
    {
        var session = await sessionRepository.GetSessionAsync(sessionId, cancellationToken);
        if (session is null)
        {
            return false;
        }

        return await IsAuthorizedAsync(session, userId, email, cancellationToken);
    }

    public async Task<RoomSeed?> AuthorizeAndLoadSeedAsync(Guid sessionId, Guid userId, string? email, CancellationToken cancellationToken)
    {
        // Single session load shared by authorization and seeding, so JoinRoom hits the DB once.
        var session = await sessionRepository.GetSessionAsync(sessionId, cancellationToken);
        if (session is null || !await IsAuthorizedAsync(session, userId, email, cancellationToken))
        {
            return null;
        }

        return BuildSeed(session);
    }

    public async Task<RoomSeed?> LoadActiveSessionSeedAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        // Guests are authorized by their validated guest cookie + sid scope (enforced at the hub),
        // not by membership — so this load only confirms the session exists and is still Active.
        var session = await sessionRepository.GetSessionAsync(sessionId, cancellationToken);
        if (session is null || session.Status != SessionStatus.Active)
        {
            return null;
        }

        return BuildSeed(session);
    }

    private static RoomSeed BuildSeed(PlanningSession session)
    {
        var tasks = session.Tasks
            .OrderBy(task => task.SortOrder)
            .Select(task => new PlanningRoomTaskSnapshot(task.Id, task.Title, task.Description, task.SortOrder, task.AgreedEstimate))
            .ToArray();

        return new RoomSeed(tasks, [.. session.ScaleValues]);
    }

    public Task<bool> SelectEstimateAsync(Guid sessionId, Guid taskId, string? estimate, CancellationToken cancellationToken)
    {
        return sessionRepository.SetAgreedEstimateAsync(sessionId, taskId, estimate, cancellationToken);
    }

    private async Task<bool> IsAuthorizedAsync(PlanningSession session, Guid userId, string? email, CancellationToken cancellationToken)
    {
        // The session creator/organizer is always authorized, even without an explicit membership row.
        if (userId != Guid.Empty && session.CreatedByUserId == userId)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            return false;
        }

        var members = await sessionMemberRepository.GetMembersAsync(session.Id, cancellationToken);
        return members.Any(member => string.Equals(member.Email, email, StringComparison.OrdinalIgnoreCase));
    }
}
