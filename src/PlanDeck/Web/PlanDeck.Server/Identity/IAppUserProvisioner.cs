using System.Security.Claims;

namespace PlanDeck.Server.Identity;

public interface IAppUserProvisioner
{
    Task<Guid> ProvisionAsync(ClaimsPrincipal principal, CancellationToken cancellationToken);

    Task<bool> IsActiveAsync(
        ClaimsPrincipal principal,
        Guid appUserId,
        CancellationToken cancellationToken);
}
