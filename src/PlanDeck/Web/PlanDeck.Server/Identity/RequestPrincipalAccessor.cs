using System.Security.Claims;

namespace PlanDeck.Server.Identity;

// Scoped per request/invocation. SignalR hub methods run outside the HTTP pipeline, so
// IHttpContextAccessor.HttpContext is null there; the hub sets Principal from Context.User so
// tenant-scoped data access (via ICurrentUserContext) resolves the caller inside the same DI scope.
public sealed class RequestPrincipalAccessor
{
    public ClaimsPrincipal? Principal { get; set; }
}
