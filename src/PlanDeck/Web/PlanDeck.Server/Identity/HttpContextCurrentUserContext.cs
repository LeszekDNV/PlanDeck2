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
}
