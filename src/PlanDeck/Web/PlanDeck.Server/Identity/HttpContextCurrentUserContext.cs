using System.Globalization;
using System.Security.Claims;
using PlanDeck.Application.Abstractions;
using PlanDeck.Common.Identity;
using PlanDeck.Infrastructure.Identity;

namespace PlanDeck.Server.Identity;

public sealed class HttpContextCurrentUserContext(
    IHttpContextAccessor httpContextAccessor,
    RequestPrincipalAccessor principalAccessor,
    IProvisioningContextAccessor provisioningAccessor) : ICurrentUserContext
{
    private readonly IHttpContextAccessor _httpContextAccessor = httpContextAccessor;
    private readonly RequestPrincipalAccessor _principalAccessor = principalAccessor;
    private readonly IProvisioningContextAccessor _provisioningAccessor = provisioningAccessor;

    public Guid TenantId =>
        _provisioningAccessor.TenantId != Guid.Empty
            ? _provisioningAccessor.TenantId
            : IsGuest
                ? ReadRequiredGuidClaim(PlanDeckClaimTypes.EntraTenantId)
                : ReadRequiredGuidClaim(PlanDeckClaimTypes.MemberTenantId);

    public Guid UserId => IsGuest
        ? throw new InvalidOperationException("Guests do not have an internal PlanDeck user ID.")
        : ReadRequiredGuidClaim(PlanDeckClaimTypes.UserId);

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public string? DisplayName => ReadStringClaim("name") ?? ReadStringClaim("preferred_username");

    public string? Email => ReadStringClaim("email") ?? ReadStringClaim("preferred_username");

    public string? ParticipantId =>
        IsGuest
            ? ReadStringClaim(PlanDeckClaimTypes.EntraObjectId)
            : ReadStringClaim(PlanDeckClaimTypes.ParticipantId);

    public bool IsGuest => PlanDeckIdentity.IsGuest(Principal);

    public Guid? SessionScope =>
        Guid.TryParse(ReadStringClaim(PlanDeckClaimTypes.SessionId), CultureInfo.InvariantCulture, out var sid) ? sid : null;

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
