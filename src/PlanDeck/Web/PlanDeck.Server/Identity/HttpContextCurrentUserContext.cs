using System.Globalization;
using System.Security.Claims;
using PlanDeck.Application.Abstractions;

namespace PlanDeck.Server.Identity;

public sealed class HttpContextCurrentUserContext(
    IHttpContextAccessor httpContextAccessor,
    RequestPrincipalAccessor principalAccessor) : ICurrentUserContext
{
    private readonly IHttpContextAccessor _httpContextAccessor = httpContextAccessor;
    private readonly RequestPrincipalAccessor _principalAccessor = principalAccessor;

    public Guid TenantId => ReadRequiredGuidClaim(PlanDeckIdentity.TenantIdClaim);

    public Guid UserId => IsGuest
        ? throw new InvalidOperationException("Guests do not have an internal PlanDeck user ID.")
        : ReadRequiredGuidClaim(PlanDeckIdentity.AppUserIdClaim);

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public string? DisplayName => ReadStringClaim("name") ?? ReadStringClaim("preferred_username");

    public string? Email => ReadStringClaim("email") ?? ReadStringClaim("preferred_username");

    public string? ParticipantId => ReadStringClaim(PlanDeckIdentity.EntraObjectIdClaim);

    public bool IsGuest =>
        string.Equals(ReadStringClaim("is_guest"), "true", StringComparison.OrdinalIgnoreCase);

    public Guid? SessionScope =>
        Guid.TryParse(ReadStringClaim("sid"), CultureInfo.InvariantCulture, out var sid) ? sid : null;

    // Prefer an explicitly supplied principal (SignalR hub invocations) and fall back to the
    // ambient HttpContext for HTTP/gRPC requests.
    private ClaimsPrincipal? Principal =>
        _principalAccessor.Principal ?? _httpContextAccessor.HttpContext?.User;

    private Guid ReadRequiredGuidClaim(string claimType)
    {
        var principal = Principal;
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return Guid.Empty;
        }

        var value = principal.FindFirstValue(claimType);
        if (!Guid.TryParse(value, CultureInfo.InvariantCulture, out var parsed)
            || parsed == Guid.Empty)
        {
            throw new InvalidOperationException(
                $"Authenticated identity claim '{claimType}' is missing or invalid.");
        }

        return parsed;
    }

    private string? ReadStringClaim(string claimType)
    {
        var principal = Principal;
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        var value = principal.FindFirstValue(claimType);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
