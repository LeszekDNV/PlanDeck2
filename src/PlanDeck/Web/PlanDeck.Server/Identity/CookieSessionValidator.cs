using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using PlanDeck.Application.Abstractions;
using PlanDeck.Common.Identity;
using PlanDeck.Infrastructure.Identity;

namespace PlanDeck.Server.Identity;

public sealed class CookieSessionValidator(
    IAppUserRepository appUserRepository,
    UserManager<ApplicationUser> userManager) : ICookieSessionValidator
{
    public async ValueTask<bool> IsValidAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        if (!PlanDeckIdentity.IsValidMember(principal))
        {
            return false;
        }

        if (!PlanDeckIdentity.TryReadGuid(principal, PlanDeckClaimTypes.MemberTenantId, out var tenantId)
            || !PlanDeckIdentity.TryReadGuid(principal, PlanDeckClaimTypes.UserId, out var userId))
        {
            return false;
        }

        if (!await appUserRepository.IsActiveAsync(tenantId, userId, cancellationToken))
        {
            return false;
        }

        return await SecurityStampMatchesAsync(principal, userId, cancellationToken);
    }

    private async Task<bool> SecurityStampMatchesAsync(
        ClaimsPrincipal principal,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var stampClaim = principal.FindFirstValue("AspNet.Identity.SecurityStamp");
        if (string.IsNullOrWhiteSpace(stampClaim))
        {
            return true;
        }

        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return false;
        }

        var currentStamp = await userManager.GetSecurityStampAsync(user);
        return string.Equals(currentStamp, stampClaim, StringComparison.Ordinal);
    }
}

