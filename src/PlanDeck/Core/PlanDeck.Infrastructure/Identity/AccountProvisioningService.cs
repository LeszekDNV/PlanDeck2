using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PlanDeck.Application.Abstractions;
using PlanDeck.Application.Domain;
using PlanDeck.Infrastructure.Persistence;

namespace PlanDeck.Infrastructure.Identity;

public sealed class AccountProvisioningService(
    PlanDeckDbContext db,
    UserManager<ApplicationUser> userManager,
    IProvisioningContextAccessor provisioningAccessor) : IAccountProvisioningService
{
    public async Task<AccountProvisioningResult> ProvisionOwnerAsync(
        string userName,
        string email,
        string firstName,
        string lastName,
        string password,
        CancellationToken cancellationToken = default)
    {
        var normalizedUserName = (userName ?? string.Empty).Trim();
        if (normalizedUserName.Contains('@'))
        {
            return AccountProvisioningResult.Failure(["User name cannot contain '@'."]);
        }

        var applicationUser = new ApplicationUser
        {
            UserName = normalizedUserName,
            Email = (email ?? string.Empty).Trim(),
            EmailConfirmed = false
        };

        IDbContextTransaction? transaction = null;
        try
        {
            transaction = await db.Database.BeginTransactionAsync(cancellationToken);

            var identityResult = await userManager.CreateAsync(applicationUser, password);
            if (!identityResult.Succeeded)
            {
                await transaction.RollbackAsync(cancellationToken);
                return AccountProvisioningResult.Failure(
                    identityResult.Errors.Select(e => e.Description).ToList());
            }

            var tenant = new PlanDeckTenant
            {
                Name = $"{firstName} {lastName}".Trim()
            };
            db.Tenants.Add(tenant);

            var appUser = new AppUser
            {
                Id = applicationUser.Id,
                TenantId = tenant.Id,
                FirstName = firstName.Trim(),
                LastName = lastName.Trim(),
                Role = TenantRole.Owner,
                IsActive = true
            };
            db.AppUsers.Add(appUser);

            provisioningAccessor.TenantId = tenant.Id;
            await db.SaveChangesAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            return new AccountProvisioningResult(
                applicationUser.Id,
                tenant.Id,
                appUser.Id,
                true,
                []);
        }
        catch (Exception exception)
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }

            return AccountProvisioningResult.Failure([exception.Message]);
        }
        finally
        {
            provisioningAccessor.TenantId = Guid.Empty;
        }
    }
}

