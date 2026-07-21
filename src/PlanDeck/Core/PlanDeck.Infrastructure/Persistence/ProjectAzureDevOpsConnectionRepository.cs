using Microsoft.EntityFrameworkCore;
using PlanDeck.Application.Abstractions;
using PlanDeck.Application.Domain;

namespace PlanDeck.Infrastructure.Persistence;

public sealed class ProjectAzureDevOpsConnectionRepository(PlanDeckDbContext db)
    : IProjectAzureDevOpsConnectionRepository
{
    public Task<ProjectAzureDevOpsConnection?> GetAsync(
        Guid projectId,
        CancellationToken cancellationToken) =>
        db.ProjectAzureDevOpsConnections
            .AsNoTracking()
            .SingleOrDefaultAsync(
                connection => connection.ProjectId == projectId,
                cancellationToken);

    public async Task AddAsync(
        ProjectAzureDevOpsConnection connection,
        CancellationToken cancellationToken)
    {
        db.ProjectAzureDevOpsConnections.Add(connection);
        await SaveAsync(cancellationToken);
    }

    public async Task UpdateAsync(
        ProjectAzureDevOpsConnection connection,
        CancellationToken cancellationToken)
    {
        db.ProjectAzureDevOpsConnections.Update(connection);
        await SaveAsync(cancellationToken);
    }

    public async Task DeleteAsync(
        ProjectAzureDevOpsConnection connection,
        CancellationToken cancellationToken)
    {
        db.ProjectAzureDevOpsConnections.Remove(connection);
        await SaveAsync(cancellationToken);
    }

    public async Task LockTargetAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var connection = await db.ProjectAzureDevOpsConnections.SingleOrDefaultAsync(
            candidate => candidate.ProjectId == projectId,
            cancellationToken)
            ?? throw new InvalidOperationException("The project connection is not configured.");
        if (connection.TargetLockedAtUtc.HasValue)
        {
            return;
        }

        connection.TargetLockedAtUtc = DateTimeOffset.UtcNow;
        await SaveAsync(cancellationToken);
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            throw new ProjectConnectionPersistenceException();
        }
    }
}
