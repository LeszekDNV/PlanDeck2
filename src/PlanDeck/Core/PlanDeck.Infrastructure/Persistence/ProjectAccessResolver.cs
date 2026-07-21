using Microsoft.EntityFrameworkCore;
using PlanDeck.Application.Abstractions;
using PlanDeck.Application.Domain;

namespace PlanDeck.Infrastructure.Persistence;

public sealed class ProjectAccessResolver(
    PlanDeckDbContext db,
    ICurrentUserContext currentUser) : IProjectAccessResolver
{
    public async Task<ProjectRole?> GetEffectiveRoleAsync(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var directRole = await db.ProjectMembers
            .Where(member => member.ProjectId == projectId
                && member.AppUserId == currentUser.UserId
                && member.Status == InvitationStatus.Accepted)
            .Select(member => (ProjectRole?)member.Role)
            .SingleOrDefaultAsync(cancellationToken);
        if (directRole is not null)
        {
            return directRole;
        }

        var inherited = await db.ProjectTeams.AnyAsync(
            assignment => assignment.ProjectId == projectId
                && db.TeamMembers.Any(member =>
                    member.TeamId == assignment.TeamId
                    && member.AppUserId == currentUser.UserId
                    && member.Status == InvitationStatus.Accepted),
            cancellationToken);

        return inherited ? ProjectRole.Member : null;
    }

    public async Task<ProjectRole> RequireRoleAsync(
        Guid projectId,
        ProjectRole minimumRole,
        CancellationToken cancellationToken)
    {
        var role = await GetEffectiveRoleAsync(projectId, cancellationToken);
        if (role is null)
        {
            throw new ProjectNotFoundException(projectId);
        }

        if (role < minimumRole)
        {
            throw new ProjectPermissionDeniedException(projectId, minimumRole);
        }

        return role.Value;
    }
}
