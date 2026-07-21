using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PlanDeck.Application.Abstractions;
using PlanDeck.Application.Domain;

namespace PlanDeck.Infrastructure.Persistence;

public sealed class AppUserRepository(PlanDeckDbContext db) : IAppUserRepository
{
    public async Task<AppUser> UpsertAsync(
        Guid tenantId,
        Guid entraObjectId,
        string displayName,
        string email,
        CancellationToken cancellationToken)
    {
        AppUser? user = null;
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await BeginTransactionAsync(cancellationToken);
            user = await db.AppUsers
                .SingleOrDefaultAsync(
                    candidate => candidate.TenantId == tenantId
                        && candidate.EntraObjectId == entraObjectId,
                    cancellationToken);

            if (user is null)
            {
                user = new AppUser
                {
                    TenantId = tenantId,
                    EntraObjectId = entraObjectId,
                    DisplayName = displayName,
                    Email = email,
                    IsActive = true
                };
                db.AppUsers.Add(user);
            }
            else
            {
                user.DisplayName = displayName;
                user.Email = email;
            }

            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException exception)
                when (exception.InnerException is SqlException { Number: 2601 or 2627 })
            {
                db.ChangeTracker.Clear();
                user = await db.AppUsers.SingleAsync(
                    candidate => candidate.TenantId == tenantId
                        && candidate.EntraObjectId == entraObjectId,
                    cancellationToken);
            }

            var now = DateTimeOffset.UtcNow;
            var projectInvitations = await db.ProjectMembers
                .Where(member => member.Status == InvitationStatus.Pending
                    && member.NormalizedEmail == user.NormalizedEmail)
                .ToListAsync(cancellationToken);
            foreach (var invitation in projectInvitations)
            {
                invitation.AppUserId = user.Id;
                invitation.Status = InvitationStatus.Accepted;
                invitation.AcceptedAtUtc = now;
            }

            var teamInvitations = await db.TeamMembers
                .Where(member => member.Status == InvitationStatus.Pending
                    && member.NormalizedEmail == user.NormalizedEmail)
                .ToListAsync(cancellationToken);
            foreach (var invitation in teamInvitations)
            {
                invitation.AppUserId = user.Id;
                invitation.Status = InvitationStatus.Accepted;
                invitation.AcceptedAtUtc = now;
            }

            if (projectInvitations.Count > 0 || teamInvitations.Count > 0)
            {
                await db.SaveChangesAsync(cancellationToken);
            }

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
        });

        return user
            ?? throw new InvalidOperationException("User provisioning did not complete.");
    }

    public Task<bool> IsActiveAsync(
        Guid tenantId,
        Guid appUserId,
        CancellationToken cancellationToken) =>
        db.AppUsers.AnyAsync(
            user => user.TenantId == tenantId && user.Id == appUserId && user.IsActive,
            cancellationToken);

    private async Task<IDbContextTransaction?> BeginTransactionAsync(
        CancellationToken cancellationToken) =>
        db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;
}
