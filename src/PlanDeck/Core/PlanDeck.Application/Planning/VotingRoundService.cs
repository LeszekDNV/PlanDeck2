using PlanDeck.Application.Abstractions;

namespace PlanDeck.Application.Planning;

public sealed class VotingRoundService(
    ISessionRepository sessionRepository,
    ISessionMemberRepository sessionMemberRepository) : IVotingRoundService
{
    public async Task<bool> IsAssignedMemberAsync(Guid sessionId, string email, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return false;
        }

        var members = await sessionMemberRepository.GetMembersAsync(sessionId, cancellationToken);
        return members.Any(member => string.Equals(member.Email, email, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<RoomSeed?> LoadRoomSeedAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var session = await sessionRepository.GetSessionAsync(sessionId, cancellationToken);
        if (session is null)
        {
            return null;
        }

        var tasks = session.Tasks
            .OrderBy(task => task.SortOrder)
            .Select(task => (task.Id, task.Title, task.SortOrder, task.AgreedEstimate))
            .ToArray();

        return new RoomSeed(tasks, [.. session.ScaleValues]);
    }

    public Task<bool> SelectEstimateAsync(Guid sessionId, Guid taskId, string? estimate, CancellationToken cancellationToken)
    {
        return sessionRepository.SetAgreedEstimateAsync(sessionId, taskId, estimate, cancellationToken);
    }
}
