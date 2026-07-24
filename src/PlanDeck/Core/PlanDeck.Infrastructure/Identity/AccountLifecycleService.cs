using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using PlanDeck.Application.Abstractions;
using PlanDeck.Application.Account;
using PlanDeck.Application.Domain;
using PlanDeck.Infrastructure.Persistence;

namespace PlanDeck.Infrastructure.Identity;

public sealed class AccountLifecycleService(
    PlanDeckDbContext db,
    UserManager<ApplicationUser> userManager,
    ILookupNormalizer lookupNormalizer,
    IEmailSender<ApplicationUser> emailSender,
    IProvisioningContextAccessor provisioningAccessor,
    TimeProvider timeProvider,
    IOptions<EmailSettings> emailSettings) : IAccountLifecycleService
{
    private readonly EmailSettings _emailSettings = emailSettings.Value;

    public async Task<ConfirmEmailResult> ConfirmEmailAsync(
        Guid userId,
        string token,
        CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return ConfirmEmailResult.InvalidToken();
        }

        if (user.EmailConfirmed)
        {
            return ConfirmEmailResult.AlreadyConfirmed();
        }

        var executionStrategy = db.Database.CreateExecutionStrategy();
        return await executionStrategy.ExecuteAsync(
            async (ct) =>
            {
                await using var transaction = await BeginTransactionAsync(ct);

                var identityResult = await userManager.ConfirmEmailAsync(user, token);
                if (!identityResult.Succeeded)
                {
                    await RollbackAsync(transaction, ct);
                    return ConfirmEmailResult.InvalidToken(identityResult.Errors.Select(e => e.Description).ToList());
                }

                var appUser = await db.AppUsers
                    .AsNoTracking()
                    .IgnoreQueryFilters()
                    .SingleOrDefaultAsync(u => u.Id == user.Id, ct);

                if (appUser is not null)
                {
                    provisioningAccessor.TenantId = appUser.TenantId;
                    var now = timeProvider.GetUtcNow();
                    var normalizedEmail = user.NormalizedEmail ?? lookupNormalizer.NormalizeEmail(user.Email ?? string.Empty);

                    await AcceptTenantInvitationAsync(appUser.TenantId, normalizedEmail, now, ct);
                    await ActivatePendingMembershipsAsync<ProjectMember>(
                        db.ProjectMembers,
                        appUser.TenantId,
                        appUser.Id,
                        normalizedEmail,
                        now,
                        ct);
                    await ActivatePendingMembershipsAsync<TeamMember>(
                        db.TeamMembers,
                        appUser.TenantId,
                        appUser.Id,
                        normalizedEmail,
                        now,
                        ct);

                    await db.SaveChangesAsync(ct);
                    provisioningAccessor.TenantId = Guid.Empty;
                }

                await CommitAsync(transaction, ct);
                return ConfirmEmailResult.Success();
            },
            cancellationToken);
    }

    public async Task<ResendConfirmationResult> ResendConfirmationAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        var normalizedEmail = lookupNormalizer.NormalizeEmail(email);
        var user = await userManager.FindByEmailAsync(normalizedEmail);
        if (user is null || user.EmailConfirmed)
        {
            return ResendConfirmationResult.Sent();
        }

        try
        {
            await SendConfirmationEmailAsync(user, cancellationToken);
            return ResendConfirmationResult.Sent();
        }
        catch (Exception exception)
        {
            return ResendConfirmationResult.SendFailed([exception.Message]);
        }
    }

    public async Task<ForgotPasswordResult> SendPasswordResetAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        var normalizedEmail = lookupNormalizer.NormalizeEmail(email);
        var user = await userManager.FindByEmailAsync(normalizedEmail);
        if (user is null || !user.EmailConfirmed)
        {
            return ForgotPasswordResult.Sent();
        }

        try
        {
            var token = await userManager.GeneratePasswordResetTokenAsync(user);
            var link = BuildLink(
                "/account/reset-password",
                $"email={Uri.EscapeDataString(user.Email ?? email)}&token={Uri.EscapeDataString(token)}");

            await emailSender.SendPasswordResetLinkAsync(user, user.Email ?? email, link);
            return ForgotPasswordResult.Sent();
        }
        catch (Exception exception)
        {
            return ForgotPasswordResult.SendFailed([exception.Message]);
        }
    }

    public async Task<ResetPasswordResult> ResetPasswordAsync(
        string email,
        string token,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        var normalizedEmail = lookupNormalizer.NormalizeEmail(email);
        var user = await userManager.FindByEmailAsync(normalizedEmail);
        if (user is null || !user.EmailConfirmed)
        {
            return ResetPasswordResult.InvalidToken();
        }

        var identityResult = await userManager.ResetPasswordAsync(user, token, newPassword);
        if (!identityResult.Succeeded)
        {
            var errors = identityResult.Errors.Select(e => e.Description).ToList();
            var firstCode = identityResult.Errors.FirstOrDefault()?.Code ?? string.Empty;
            return firstCode switch
            {
                nameof(IdentityErrorDescriber.InvalidToken) => ResetPasswordResult.InvalidToken(errors),
                nameof(IdentityErrorDescriber.PasswordTooShort)
                    or nameof(IdentityErrorDescriber.PasswordRequiresNonAlphanumeric)
                    or nameof(IdentityErrorDescriber.PasswordRequiresDigit)
                    or nameof(IdentityErrorDescriber.PasswordRequiresLower)
                    or nameof(IdentityErrorDescriber.PasswordRequiresUpper)
                    or nameof(IdentityErrorDescriber.PasswordRequiresUniqueChars) => ResetPasswordResult.WeakPassword(errors),
                _ => ResetPasswordResult.InvalidToken(errors)
            };
        }

        await userManager.UpdateSecurityStampAsync(user);
        return ResetPasswordResult.Success();
    }

    internal async Task SendConfirmationEmailAsync(ApplicationUser user, CancellationToken cancellationToken = default)
    {
        var token = await userManager.GenerateEmailConfirmationTokenAsync(user);
        var link = BuildLink(
            "/account/confirm-email",
            $"userId={user.Id}&token={Uri.EscapeDataString(token)}");

        await emailSender.SendConfirmationLinkAsync(user, user.Email ?? string.Empty, link);
    }

    private string BuildLink(string path, string query)
    {
        var baseUrl = _emailSettings.PublicBaseUrl.TrimEnd('/');
        return $"{baseUrl}{path}?{query}";
    }

    private async Task AcceptTenantInvitationAsync(
        Guid tenantId,
        string normalizedEmail,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var invitation = await db.TenantInvitations
            .IgnoreQueryFilters()
            .Where(i =>
                i.TenantId == tenantId
                && i.NormalizedEmail == normalizedEmail
                && i.Status == InvitationStatus.Pending)
            .SingleOrDefaultAsync(cancellationToken);

        if (invitation is not null)
        {
            invitation.Status = InvitationStatus.Accepted;
            invitation.AcceptedAtUtc = now;
        }
    }

    private async Task ActivatePendingMembershipsAsync<TMember>(
        DbSet<TMember> members,
        Guid tenantId,
        Guid appUserId,
        string normalizedEmail,
        DateTimeOffset now,
        CancellationToken cancellationToken)
        where TMember : class, ITenantScoped
    {
        var pending = await members
            .IgnoreQueryFilters()
            .Where(m => m.TenantId == tenantId && EF.Property<string>(m, "NormalizedEmail") == normalizedEmail)
            .ToListAsync(cancellationToken);

        foreach (var member in pending)
        {
            var statusProperty = typeof(TMember).GetProperty(nameof(ProjectMember.Status));
            if (statusProperty is null)
            {
                continue;
            }

            var currentStatus = (InvitationStatus)statusProperty.GetValue(member)!;
            if (currentStatus != InvitationStatus.Pending)
            {
                continue;
            }

            var appUserIdProperty = typeof(TMember).GetProperty(nameof(ProjectMember.AppUserId));
            appUserIdProperty?.SetValue(member, appUserId);
            statusProperty.SetValue(member, InvitationStatus.Accepted);

            var acceptedAtProperty = typeof(TMember).GetProperty(nameof(ProjectMember.AcceptedAtUtc));
            acceptedAtProperty?.SetValue(member, now);
        }
    }

    private async Task<IDbContextTransaction?> BeginTransactionAsync(CancellationToken cancellationToken) =>
        db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;

    private static async Task RollbackAsync(IDbContextTransaction? transaction, CancellationToken cancellationToken)
    {
        if (transaction is not null)
        {
            await transaction.RollbackAsync(cancellationToken);
        }
    }

    private static async Task CommitAsync(IDbContextTransaction? transaction, CancellationToken cancellationToken)
    {
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }
    }
}

