using System.Globalization;
using System.Security.Claims;
using PlanDeck.Application.Abstractions;

namespace PlanDeck.Server.Identity;

public sealed class HttpContextCurrentUserContext(IHttpContextAccessor httpContextAccessor) : ICurrentUserContext
{
    private readonly IHttpContextAccessor _httpContextAccessor = httpContextAccessor;

    public Guid TenantId => ReadGuidClaim("tid");

    public Guid UserId => ReadGuidClaim("oid");

    public bool IsAuthenticated =>
        _httpContextAccessor.HttpContext?.User.Identity?.IsAuthenticated == true;

    public string? DisplayName => ReadStringClaim("name") ?? ReadStringClaim("preferred_username");

    public string? Email => ReadStringClaim("email") ?? ReadStringClaim("preferred_username");

    private Guid ReadGuidClaim(string claimType)
    {
        var principal = _httpContextAccessor.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return Guid.Empty;
        }

        var value = principal.FindFirstValue(claimType);
        return Guid.TryParse(value, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : Guid.Empty;
    }

    private string? ReadStringClaim(string claimType)
    {
        var principal = _httpContextAccessor.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        var value = principal.FindFirstValue(claimType);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
