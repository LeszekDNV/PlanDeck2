using System.Security.Claims;
using PlanDeck.Application.Abstractions;

namespace PlanDeck.Server.Identity;

public sealed class AppUserProvisioner(
    IAppUserRepository repository,
    RequestPrincipalAccessor principalAccessor) : IAppUserProvisioner
{
    public async Task<Guid> ProvisionAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        principalAccessor.Principal = principal;

        if (!PlanDeckIdentity.TryReadGuid(principal, PlanDeckIdentity.TenantIdClaim, out var tenantId)
            || !PlanDeckIdentity.TryReadGuid(
                principal,
                PlanDeckIdentity.EntraObjectIdClaim,
                out var entraObjectId))
        {
            throw new InvalidOperationException("The authenticated identity is missing required identifiers.");
        }

        var email = principal.FindFirstValue("email")
            ?? principal.FindFirstValue("preferred_username");
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new InvalidOperationException("The authenticated identity is missing an email address.");
        }

        var displayName = principal.FindFirstValue("name")?.Trim();
        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = email.Trim();
        }

        var user = await repository.UpsertAsync(
            tenantId,
            entraObjectId,
            displayName,
            email.Trim(),
            cancellationToken);

        if (!user.IsActive)
        {
            throw new InvalidOperationException("The PlanDeck account is inactive.");
        }

        return user.Id;
    }

    public Task<bool> IsActiveAsync(
        ClaimsPrincipal principal,
        Guid appUserId,
        CancellationToken cancellationToken)
    {
        principalAccessor.Principal = principal;
        return PlanDeckIdentity.TryReadGuid(
            principal,
            PlanDeckIdentity.TenantIdClaim,
            out var tenantId)
            ? repository.IsActiveAsync(tenantId, appUserId, cancellationToken)
            : Task.FromResult(false);
    }
}
