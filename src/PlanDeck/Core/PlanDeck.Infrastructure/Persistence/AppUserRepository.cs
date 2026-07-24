using Microsoft.EntityFrameworkCore;
using PlanDeck.Application.Abstractions;
using PlanDeck.Application.Domain;

namespace PlanDeck.Infrastructure.Persistence;

public sealed class AppUserRepository(PlanDeckDbContext db) : IAppUserRepository
{
    public Task<AppUser?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.AppUsers.AsNoTracking().SingleOrDefaultAsync(
            user => user.Id == id,
            cancellationToken);

    public Task<bool> IsActiveAsync(
        Guid tenantId,
        Guid id,
        CancellationToken cancellationToken = default) =>
        db.AppUsers.AnyAsync(
            user => user.TenantId == tenantId && user.Id == id && user.IsActive,
            cancellationToken);
}
