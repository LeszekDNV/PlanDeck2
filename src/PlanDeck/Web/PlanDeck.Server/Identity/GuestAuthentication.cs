using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace PlanDeck.Server.Identity;

/// <summary>
/// Dedicated cookie scheme for account-less guests who join a session vote via a share link.
/// Kept distinct from the member (Entra) cookie so a guest sign-in can never be mistaken for an
/// authenticated organisation session and can be challenged/authorised independently.
/// </summary>
public static class GuestAuthentication
{
    public const string SchemeName = "Guest";
    public const string CookieName = "PlanDeck.Guest";
    public const string IsGuestClaim = "is_guest";
    public const string SessionIdClaim = "sid";

    /// <summary>
    /// Authorization policy admitting both members (cookie/OIDC) and guests to the planning room,
    /// while still rejecting fully anonymous callers. Schemes are bound per environment at startup.
    /// </summary>
    public const string RoomParticipantPolicy = PlanDeckPolicies.RoomIdentity;

    public static void ConfigureCookie(CookieAuthenticationOptions options)
    {
        options.Cookie.Name = CookieName;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;

        options.Events.OnValidatePrincipal = context =>
        {
            if (!PlanDeckIdentity.IsValidGuest(context.Principal))
            {
                context.RejectPrincipal();
            }

            return Task.CompletedTask;
        };

        // The guest surface is an anonymous API, not an interactive site: never redirect to a
        // login/access-denied page, return the bare status code instead.
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    }

    public static ClaimsPrincipal BuildPrincipal(Guid participantId, Guid tenantId, string displayName, Guid sessionId)
    {
        var identity = new ClaimsIdentity(
        [
            new Claim("oid", participantId.ToString()),
            new Claim("tid", tenantId.ToString()),
            new Claim("name", displayName),
            new Claim(SessionIdClaim, sessionId.ToString()),
            new Claim(IsGuestClaim, "true")
        ], SchemeName);

        return new ClaimsPrincipal(identity);
    }
}
