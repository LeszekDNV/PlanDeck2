using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace PlanDeck.Server.Identity;

public sealed class TestAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";

    /// <summary>
    /// Optional request cookie that lets an E2E browser context choose which deterministic
    /// identity it authenticates as. Absent / any other value selects the default user (A);
    /// the value "b" selects the second user, enabling two distinct voters across two contexts.
    /// </summary>
    public const string UserSelectionCookie = "e2e-user";

    /// <summary>
    /// Optional request header carrying a session id. When present, the handler authenticates a
    /// deterministic guest (no membership) scoped to that session via the <c>sid</c>/<c>is_guest</c>
    /// claims, mirroring the production Guest cookie identity for hub/E2E tests.
    /// </summary>
    public const string GuestSessionHeader = "X-Test-Guest-Sid";

    /// <summary>
    /// Cookie counterpart of <see cref="GuestSessionHeader"/>. A browser sends cookies on every
    /// request it initiates — including the SignalR WebSocket handshake, which Playwright's
    /// per-context extra headers do not reliably reach — so an E2E guest context sets this cookie
    /// to stay a guest across both ordinary requests and the hub connection.
    /// </summary>
    public const string GuestSessionCookie = "e2e-guest-sid";

    private const string TestTenantId = "11111111-1111-1111-1111-111111111111";
    private const string TestObjectId = "22222222-2222-2222-2222-222222222222";
    private const string TestDisplayName = "Test User";
    private const string TestEmail = "test.user@plandeck.local";

    private const string SecondObjectId = "33333333-3333-3333-3333-333333333333";
    private const string SecondDisplayName = "Test User B";
    private const string SecondEmail = "test.userb@plandeck.local";

    private const string GuestObjectId = "44444444-4444-4444-4444-444444444444";
    private const string GuestDisplayName = "Guest User";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var guestSid =
            Request.Headers.TryGetValue(GuestSessionHeader, out var headerSid) && !string.IsNullOrWhiteSpace(headerSid)
                ? headerSid.ToString()
                : Request.Cookies.TryGetValue(GuestSessionCookie, out var cookieSid) && !string.IsNullOrWhiteSpace(cookieSid)
                    ? cookieSid
                    : null;

        if (guestSid is not null)
        {
            return Task.FromResult(BuildResult(
            [
                new Claim("tid", TestTenantId),
                new Claim("oid", GuestObjectId),
                new Claim("name", GuestDisplayName),
                new Claim(GuestAuthentication.SessionIdClaim, guestSid),
                new Claim(GuestAuthentication.IsGuestClaim, "true")
            ]));
        }

        var isSecondUser =
            Request.Cookies.TryGetValue(UserSelectionCookie, out var selection)
            && string.Equals(selection, "b", StringComparison.OrdinalIgnoreCase);

        var objectId = isSecondUser ? SecondObjectId : TestObjectId;
        var displayName = isSecondUser ? SecondDisplayName : TestDisplayName;
        var email = isSecondUser ? SecondEmail : TestEmail;

        return Task.FromResult(BuildResult(
        [
            new Claim("tid", TestTenantId),
            new Claim("oid", objectId),
            new Claim("name", displayName),
            new Claim("email", email)
        ]));
    }

    private AuthenticateResult BuildResult(Claim[] claims)
    {
        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        return AuthenticateResult.Success(ticket);
    }
}
