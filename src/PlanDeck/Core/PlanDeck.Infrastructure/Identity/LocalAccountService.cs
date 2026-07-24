using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PlanDeck.Application.Abstractions;
using PlanDeck.Application.Account;
using PlanDeck.Application.Domain;
using PlanDeck.Infrastructure.Persistence;

namespace PlanDeck.Infrastructure.Identity;

public sealed class LocalAccountService(
    PlanDeckDbContext db,
    UserManager<ApplicationUser> userManager,
    ILookupNormalizer lookupNormalizer,
    IProvisioningContextAccessor provisioningAccessor,
    TimeProvider timeProvider,
    IConfiguration configuration,
    IEmailSender<ApplicationUser> emailSender,
    IOptions<EmailSettings> emailSettings,
    ILogger<LocalAccountService> logger) : ILocalAccountService
{
    private const int MinUserNameLength = 3;
    private const int MaxUserNameLength = 32;
    private const int MaxNameLength = 100;

    public async Task<LocalRegisterResult> RegisterAsync(
        LocalRegisterRequest request,
        CancellationToken cancellationToken = default)
    {
        var email = (request.Email ?? string.Empty).Trim();
        var firstName = (request.FirstName ?? string.Empty).Trim();
        var lastName = (request.LastName ?? string.Empty).Trim();
        var userName = (request.UserName ?? string.Empty).Trim();
        var password = request.Password ?? string.Empty;
        var invitationToken = request.InvitationToken;
        var autoConfirmEmail = configuration.GetValue<bool>("Testing:E2e:AutoConfirmEmail");

        if (string.IsNullOrWhiteSpace(email))
        {
            return LocalRegisterResult.Failure(LocalRegisterStatus.InvalidEmail, "Email is required.");
        }

        if (string.IsNullOrWhiteSpace(userName))
        {
            return LocalRegisterResult.Failure(LocalRegisterStatus.InvalidUserName, "User name is required.");
        }

        if (userName.Contains('@'))
        {
            return LocalRegisterResult.Failure(
                LocalRegisterStatus.InvalidUserName,
                "User name cannot contain ''.");
        }

        if (userName.Length < MinUserNameLength || userName.Length > MaxUserNameLength)
        {
            return LocalRegisterResult.Failure(
                LocalRegisterStatus.InvalidUserName,
                $"User name must be between {MinUserNameLength} and {MaxUserNameLength} characters.");
        }

        if (firstName.Length > MaxNameLength || lastName.Length > MaxNameLength)
        {
            return LocalRegisterResult.Failure(
                LocalRegisterStatus.InvalidUserName,
                $"First or last name cannot exceed {MaxNameLength} characters.");
        }

        var normalizedEmail = lookupNormalizer.NormalizeEmail(email);
        if (string.IsNullOrWhiteSpace(normalizedEmail))
        {
            return LocalRegisterResult.Failure(LocalRegisterStatus.InvalidEmail, "Email is invalid.");
        }

        var hasInvitation = !string.IsNullOrWhiteSpace(invitationToken);
        if (!hasInvitation && !IsPublicRegistrationEnabled())
        {
            return LocalRegisterResult.Failure(
                LocalRegisterStatus.PublicRegistrationDisabled,
                "Public registration is disabled.");
        }

        TenantInvitation? invitation = null;
        if (hasInvitation)
        {
            invitation = await FindValidInvitationAsync(invitationToken!, normalizedEmail, cancellationToken);
            if (invitation is null)
            {
                return LocalRegisterResult.Failure(
                    LocalRegisterStatus.InvitationInvalidOrExpired,
                    "Invitation is invalid, expired, or does not match the email address.");
            }
        }

        var applicationUser = new ApplicationUser
        {
            UserName = userName,
            Email = email,
            EmailConfirmed = autoConfirmEmail
        };

        var executionStrategy = db.Database.CreateExecutionStrategy();

        try
        {
            return await executionStrategy.ExecuteAsync(
                async (token) =>
                {
                    await using var transaction = await db.Database.BeginTransactionAsync(token);

                    var identityResult = await userManager.CreateAsync(applicationUser, password);
                    if (!identityResult.Succeeded)
                    {
                        await transaction.RollbackAsync(token);
                        return MapIdentityErrors(identityResult.Errors);
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
                        tenant = new PlanDeckTenant { Name = $"{firstName} {lastName}".Trim() };
                        db.Tenants.Add(tenant);
                        role = TenantRole.Owner;
                        provisioningAccessor.TenantId = tenant.Id;
                    }

                    var appUser = new AppUser
                    {
                        Id = applicationUser.Id,
                        TenantId = tenant.Id,
                        FirstName = firstName,
                        LastName = lastName,
                        Role = role,
                        IsActive = true
                    };
                    db.AppUsers.Add(appUser);

                    await db.SaveChangesAsync(token);
                    await transaction.CommitAsync(token);

                    if (!autoConfirmEmail)
                    {
                        await TrySendConfirmationEmailAsync(applicationUser, token);
                    }

                    return LocalRegisterResult.Success(applicationUser.Id);
                },
                cancellationToken);
        }
        catch (Exception exception)
        {
            return LocalRegisterResult.Failure(LocalRegisterStatus.Failure, exception.Message);
        }
        finally
        {
            provisioningAccessor.TenantId = Guid.Empty;
        }
    }

    private bool IsPublicRegistrationEnabled() =>
        configuration.GetValue<bool?>("Authentication:AllowPublicRegistration") ?? true;

    private async Task TrySendConfirmationEmailAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        try
        {
            var token = await userManager.GenerateEmailConfirmationTokenAsync(user);
            var baseUrl = emailSettings.Value.PublicBaseUrl.TrimEnd('/');
            var link = $"{baseUrl}/account/confirm-email?userId={user.Id}&token={Uri.EscapeDataString(token)}";

            await emailSender.SendConfirmationLinkAsync(user, user.Email ?? string.Empty, link);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to send confirmation email to {Email}.", user.Email);
        }
    }

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

    private static byte[] HashToken(string token) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(token));

    private static LocalRegisterResult MapIdentityErrors(IEnumerable<IdentityError> errors)
    {
        var errorList = errors.ToList();
        var firstCode = errorList.FirstOrDefault()?.Code ?? string.Empty;

        var status = firstCode switch
        {
            nameof(IdentityErrorDescriber.DuplicateUserName) => LocalRegisterStatus.DuplicateUserName,
            nameof(IdentityErrorDescriber.DuplicateEmail) => LocalRegisterStatus.DuplicateEmail,
            nameof(IdentityErrorDescriber.InvalidUserName) => LocalRegisterStatus.InvalidUserName,
            nameof(IdentityErrorDescriber.InvalidEmail) => LocalRegisterStatus.InvalidEmail,
            nameof(IdentityErrorDescriber.PasswordTooShort)
                or nameof(IdentityErrorDescriber.PasswordRequiresNonAlphanumeric)
                or nameof(IdentityErrorDescriber.PasswordRequiresDigit)
                or nameof(IdentityErrorDescriber.PasswordRequiresLower)
                or nameof(IdentityErrorDescriber.PasswordRequiresUpper)
                or nameof(IdentityErrorDescriber.PasswordRequiresUniqueChars) => LocalRegisterStatus.WeakPassword,
            _ => LocalRegisterStatus.InvalidPassword
        };

        return LocalRegisterResult.Failure(status, errorList.Select(e => e.Description).ToArray());
    }
}



