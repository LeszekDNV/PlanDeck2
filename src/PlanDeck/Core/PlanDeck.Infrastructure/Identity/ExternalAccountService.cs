using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PlanDeck.Application.Abstractions;
using PlanDeck.Application.Account;
using PlanDeck.Application.Domain;
using PlanDeck.Infrastructure.Persistence;
using ExternalLoginInfo = PlanDeck.Application.Account.ExternalLoginInfo;

namespace PlanDeck.Infrastructure.Identity;

public sealed class ExternalAccountService(
    PlanDeckDbContext db,
    UserManager<ApplicationUser> userManager,
    ILookupNormalizer lookupNormalizer,
    IProvisioningContextAccessor provisioningAccessor,
    IConfiguration configuration,
    TimeProvider timeProvider,
    ILogger<ExternalAccountService> logger) : IExternalAccountService
{
    private const string MicrosoftEntraProvider = "MicrosoftEntra";
    private const int MinUserNameLength = 3;
    private const int MaxUserNameLength = 32;

    public async Task<EntraLoginResult> LoginAsync(
        ExternalLoginInfo loginInfo,
        CancellationToken cancellationToken = default)
    {
        var user = await FindByExternalLoginAsync(loginInfo, cancellationToken);
        if (user is null)
        {
            return EntraLoginResult.Failure(
                EntraCallbackStatus.ExternalIdentityNotFound,
                "No PlanDeck account is linked to this Microsoft identity.");
        }

        var appUser = await db.AppUsers
            .AsNoTracking()
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(u => u.Id == user.Id, cancellationToken);

        if (appUser is null || !appUser.IsActive)
        {
            return EntraLoginResult.Failure(
                EntraCallbackStatus.AccountInactive,
                "The account is inactive or not provisioned.");
        }

        return EntraLoginResult.Success(user.Id);
    }

    public async Task<EntraRegisterResult> RegisterAsync(
        ExternalLoginInfo loginInfo,
        string? invitationToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(loginInfo.Email))
        {
            return EntraRegisterResult.Failure(
                EntraCallbackStatus.EmailRequired,
                "An email address is required to register.");
        }

        var email = loginInfo.Email.Trim();
        var normalizedEmail = lookupNormalizer.NormalizeEmail(email);

        var existingByEmail = await userManager.FindByEmailAsync(normalizedEmail);
        if (existingByEmail is not null)
        {
            return EntraRegisterResult.Failure(
                EntraCallbackStatus.DuplicateEmail,
                "An account with this email already exists. Sign in and link Microsoft identity from security settings.");
        }

        var userName = await GenerateAvailableUserNameAsync(email, cancellationToken);
        if (userName is null)
        {
            return EntraRegisterResult.Failure(
                EntraCallbackStatus.DuplicateUserName,
                "Could not generate a unique username from the provided email.");
        }

        var hasInvitation = !string.IsNullOrWhiteSpace(invitationToken);
        if (!hasInvitation && !IsPublicRegistrationEnabled())
        {
            return EntraRegisterResult.Failure(
                EntraCallbackStatus.PublicRegistrationDisabled,
                "Public registration is disabled.");
        }

        TenantInvitation? invitation = null;
        if (hasInvitation)
        {
            invitation = await FindValidInvitationAsync(invitationToken!, normalizedEmail, cancellationToken);
            if (invitation is null)
            {
                return EntraRegisterResult.Failure(
                    EntraCallbackStatus.InvalidState,
                    "Invitation is invalid, expired, or does not match the email address.");
            }
        }

        var applicationUser = new ApplicationUser
        {
            UserName = userName,
            Email = email,
            EmailConfirmed = false
        };

        var executionStrategy = db.Database.CreateExecutionStrategy();
        try
        {
            return await executionStrategy.ExecuteAsync(
                async (token) =>
                {
                    await using var transaction = await db.Database.BeginTransactionAsync(token);

                    var identityResult = await userManager.CreateAsync(applicationUser);
                    if (!identityResult.Succeeded)
                    {
                        await transaction.RollbackAsync(token);
                        return MapIdentityErrors(identityResult.Errors);
                    }

                    var addLoginResult = await userManager.AddLoginAsync(
                        applicationUser,
                        new UserLoginInfo(MicrosoftEntraProvider, loginInfo.ProviderKey, null));

                    if (!addLoginResult.Succeeded)
                    {
                        await transaction.RollbackAsync(token);
                        return MapIdentityErrors(addLoginResult.Errors);
                    }

                    PlanDeckTenant tenant;
                    TenantRole role;
                    if (invitation is not null)
                    {
                        tenant = await db.Tenants.IgnoreQueryFilters()
                            .SingleAsync(t => t.Id == invitation.TenantId, token);
                        role = invitation.Role;
                        provisioningAccessor.TenantId = invitation.TenantId;
                    }
                    else
                    {
                        var firstName = (loginInfo.FirstName ?? userName).Trim();
                        var lastName = (loginInfo.LastName ?? string.Empty).Trim();
                        tenant = new PlanDeckTenant { Name = $"{firstName} {lastName}".Trim() };
                        db.Tenants.Add(tenant);
                        role = TenantRole.Owner;
                        provisioningAccessor.TenantId = tenant.Id;
                    }

                    var appUser = new AppUser
                    {
                        Id = applicationUser.Id,
                        TenantId = tenant.Id,
                        FirstName = (loginInfo.FirstName ?? userName).Trim(),
                        LastName = (loginInfo.LastName ?? string.Empty).Trim(),
                        Role = role,
                        IsActive = true
                    };
                    db.AppUsers.Add(appUser);

                    await db.SaveChangesAsync(token);
                    await transaction.CommitAsync(token);

                    return EntraRegisterResult.Success(applicationUser.Id);
                },
                cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to provision external account for {Email}.", email);
            return EntraRegisterResult.Failure(EntraCallbackStatus.InvalidState, exception.Message);
        }
        finally
        {
            provisioningAccessor.TenantId = Guid.Empty;
        }
    }

    public async Task<EntraLinkResult> LinkAsync(
        Guid currentUserId,
        ExternalLoginInfo loginInfo,
        CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(currentUserId.ToString());
        if (user is null)
        {
            return EntraLinkResult.Failure(
                EntraCallbackStatus.AccountNotFound,
                "Current account was not found.");
        }

        var existingUserForLogin = await FindByExternalLoginAsync(loginInfo, cancellationToken);
        if (existingUserForLogin is not null)
        {
            return existingUserForLogin.Id == currentUserId
                ? EntraLinkResult.Success()
                : EntraLinkResult.Failure(
                    EntraCallbackStatus.ExternalIdentityUsedElsewhere,
                    "This Microsoft identity is already linked to another PlanDeck account.");
        }

        var addLoginResult = await userManager.AddLoginAsync(
            user,
            new UserLoginInfo(MicrosoftEntraProvider, loginInfo.ProviderKey, null));

        if (!addLoginResult.Succeeded)
        {
            return EntraLinkResult.Failure(
                EntraCallbackStatus.InvalidState,
                addLoginResult.Errors.Select(e => e.Description).ToArray());
        }

        await userManager.UpdateSecurityStampAsync(user);
        return EntraLinkResult.Success();
    }

    public async Task<EntraLinkResult> UnlinkAsync(
        Guid currentUserId,
        string provider,
        string providerKey,
        CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(currentUserId.ToString());
        if (user is null)
        {
            return EntraLinkResult.Failure(
                EntraCallbackStatus.AccountNotFound,
                "Current account was not found.");
        }

        var hasLocalPassword = await userManager.HasPasswordAsync(user);
        if (!hasLocalPassword)
        {
            return EntraLinkResult.Failure(
                EntraCallbackStatus.InvalidState,
                "Add a local password before unlinking the external identity.");
        }

        var logins = await userManager.GetLoginsAsync(user);
        var login = logins.SingleOrDefault(l =>
            l.LoginProvider == provider && l.ProviderKey == providerKey);

        if (login is null)
        {
            return EntraLinkResult.Success();
        }

        var removeResult = await userManager.RemoveLoginAsync(user, provider, providerKey);
        if (!removeResult.Succeeded)
        {
            return EntraLinkResult.Failure(
                EntraCallbackStatus.InvalidState,
                removeResult.Errors.Select(e => e.Description).ToArray());
        }

        await userManager.UpdateSecurityStampAsync(user);
        return EntraLinkResult.Success();
    }

    private async Task<ApplicationUser?> FindByExternalLoginAsync(
        ExternalLoginInfo loginInfo,
        CancellationToken cancellationToken = default) =>
        await userManager.FindByLoginAsync(loginInfo.Provider, loginInfo.ProviderKey)
            .WaitAsync(cancellationToken);

    private async Task<TenantInvitation?> FindValidInvitationAsync(
        string token,
        string normalizedEmail,
        CancellationToken cancellationToken)
    {
        var hash = HashToken(token);
        var now = timeProvider.GetUtcNow();

        return await db.TenantInvitations
            .IgnoreQueryFilters()
            .Where(i =>
                i.TokenHash == hash
                && i.NormalizedEmail == normalizedEmail
                && i.Status == InvitationStatus.Pending
                && i.ExpiresAtUtc > now)
            .SingleOrDefaultAsync(cancellationToken);
    }

    private async Task<string?> GenerateAvailableUserNameAsync(string email, CancellationToken cancellationToken)
    {
        var localPart = email.Split('@')[0];
        if (localPart.Length > MaxUserNameLength)
        {
            localPart = localPart[..MaxUserNameLength];
        }

        var baseName = localPart;
        if (baseName.Length < MinUserNameLength)
        {
            baseName = baseName.PadRight(MinUserNameLength, '0');
        }

        var candidate = baseName;
        if (await IsUserNameAvailableAsync(candidate, cancellationToken))
        {
            return candidate;
        }

        for (var i = 0; i < 1000; i++)
        {
            var suffix = i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var maxBaseLength = MaxUserNameLength - suffix.Length;
            if (maxBaseLength < MinUserNameLength)
            {
                return null;
            }

            candidate = $"{baseName[..Math.Min(baseName.Length, maxBaseLength)]}{suffix}";
            if (await IsUserNameAvailableAsync(candidate, cancellationToken))
            {
                return candidate;
            }
        }

        return null;
    }

    private async Task<bool> IsUserNameAvailableAsync(string userName, CancellationToken cancellationToken)
    {
        var normalized = lookupNormalizer.NormalizeName(userName);
        return !await db.Users.AnyAsync(u => u.NormalizedUserName == normalized, cancellationToken);
    }

    private bool IsPublicRegistrationEnabled() =>
        configuration.GetValue<bool?>("Authentication:AllowPublicRegistration") ?? true;

    private static EntraRegisterResult MapIdentityErrors(IEnumerable<IdentityError> errors)
    {
        var errorList = errors.ToList();
        var firstCode = errorList.FirstOrDefault()?.Code ?? string.Empty;

        var status = firstCode switch
        {
            nameof(IdentityErrorDescriber.DuplicateUserName) => EntraCallbackStatus.DuplicateUserName,
            nameof(IdentityErrorDescriber.DuplicateEmail) => EntraCallbackStatus.DuplicateEmail,
            _ => EntraCallbackStatus.InvalidState
        };

        return EntraRegisterResult.Failure(status, errorList.Select(e => e.Description).ToArray());
    }

    private static byte[] HashToken(string token) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(token));
}
