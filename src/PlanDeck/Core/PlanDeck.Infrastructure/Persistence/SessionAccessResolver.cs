using Microsoft.EntityFrameworkCore;
using PlanDeck.Application.Abstractions;
using PlanDeck.Application.Domain;

namespace PlanDeck.Infrastructure.Persistence;

public sealed class SessionAccessResolver(
    PlanDeckDbContext db,
    IProjectAccessResolver projectAccess) : ISessionAccessResolver
{
    public async Task<(Guid ProjectId, ProjectRole Role)?> ResolveProjectAccessAsync(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var projectId = await db.Sessions
            .Where(session => session.Id == sessionId)
            .Select(session => (Guid?)session.ProjectId)
            .SingleOrDefaultAsync(cancellationToken);
        if (projectId is null)
        {
            return null;
        }

        var role = await projectAccess.GetEffectiveRoleAsync(
            projectId.Value,
            cancellationToken);
        return role is null ? null : (projectId.Value, role.Value);
    }
}
