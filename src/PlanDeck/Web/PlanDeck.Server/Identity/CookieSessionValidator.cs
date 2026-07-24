using System.Security.Claims;
using PlanDeck.Application.Abstractions;
using PlanDeck.Common.Identity;

namespace PlanDeck.Server.Identity;

public sealed class CookieSessionValidator(IAppUserRepository appUserRepository) : ICookieSessionValidator
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

        return await appUserRepository.IsActiveAsync(tenantId, userId, cancellationToken);
    }
}

