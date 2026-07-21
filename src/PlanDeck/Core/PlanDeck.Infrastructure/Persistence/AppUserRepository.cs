using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
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
        var user = await db.AppUsers
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

        return user;
    }

    public Task<bool> IsActiveAsync(
        Guid tenantId,
        Guid appUserId,
        CancellationToken cancellationToken) =>
        db.AppUsers.AnyAsync(
            user => user.TenantId == tenantId && user.Id == appUserId && user.IsActive,
            cancellationToken);
}
