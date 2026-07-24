using Microsoft.EntityFrameworkCore;
using PlanDeck.Application.Abstractions;
using PlanDeck.Infrastructure.Persistence;

namespace PlanDeck.Infrastructure.Identity;

public sealed class IdentityAccountRepository(PlanDeckDbContext db) : IIdentityAccountRepository
{
    public Task<IdentityAccount?> FindByNormalizedEmailAsync(
        string normalizedEmail,
        CancellationToken cancellationToken = default) =>
        db.Users.AsNoTracking()
            .Where(u => u.NormalizedEmail == normalizedEmail)
            .Select(u => Map(u))
            .SingleOrDefaultAsync(cancellationToken);

    public Task<IdentityAccount?> FindByNormalizedUserNameAsync(
        string normalizedUserName,
        CancellationToken cancellationToken = default) =>
        db.Users.AsNoTracking()
            .Where(u => u.NormalizedUserName == normalizedUserName)
            .Select(u => Map(u))
            .SingleOrDefaultAsync(cancellationToken);

    public Task<IdentityAccount?> FindByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default) =>
        db.Users.AsNoTracking()
            .Where(u => u.Id == id)
            .Select(u => Map(u))
            .SingleOrDefaultAsync(cancellationToken);

    private static IdentityAccount Map(ApplicationUser u) =>
        new(u.Id, u.NormalizedUserName ?? string.Empty, u.NormalizedEmail, u.EmailConfirmed);
}

