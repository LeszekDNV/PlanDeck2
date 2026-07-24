using System.Security.Claims;

namespace PlanDeck.Server.Identity;

public interface ICookieSessionValidator
{
    ValueTask<bool> IsValidAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default);
}

