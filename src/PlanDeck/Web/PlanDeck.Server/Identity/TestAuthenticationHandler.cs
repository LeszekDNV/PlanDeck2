using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using PlanDeck.Server.Testing;

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
    /// identity it authenticates as. Recognized values: owner, admin, member, anonymous.
    /// When absent, owner is used by default. Unknown values fail authentication.
    /// </summary>
    public const string UserSelectionCookie = "e2e-user";
    public const string AnonymousSelection = "anonymous";

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

    public const string IdentityShapeHeader = "X-Test-Identity";

    private const string GuestObjectId = "44444444-4444-4444-4444-444444444444";
    private const string GuestDisplayName = "Guest User";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var requestedShape = Request.Headers[IdentityShapeHeader].ToString();
        if (string.Equals(requestedShape, "anonymous", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        if (string.Equals(requestedShape, "malformed", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(AuthenticateResult.Fail("The test identity is malformed."));
        }

        if (Request.Cookies.TryGetValue(UserSelectionCookie, out var userSelection)
            && string.Equals(userSelection, AnonymousSelection, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var guestSid =
            Request.Headers.TryGetValue(GuestSessionHeader, out var headerSid) && !string.IsNullOrWhiteSpace(headerSid)
                ? headerSid.ToString()
                : Request.Cookies.TryGetValue(GuestSessionCookie, out var cookieSid) && !string.IsNullOrWhiteSpace(cookieSid)
                    ? cookieSid
                    : null;

        if (guestSid is not null)
        {
            if (!Guid.TryParse(guestSid, out var parsedGuestSid)
                || parsedGuestSid == Guid.Empty)
            {
                return Task.FromResult(
                    AuthenticateResult.Fail("The test guest session ID is invalid."));
            }

            return Task.FromResult(BuildResult(
            [
                new Claim("tid", TestMemberIdentities.TenantId.ToString()),
                new Claim("oid", GuestObjectId),
                new Claim("name", GuestDisplayName),
                new Claim(GuestAuthentication.SessionIdClaim, parsedGuestSid.ToString()),
                new Claim(GuestAuthentication.IsGuestClaim, "true")
            ]));
        }

        var selectedIdentityResult = ResolveSelectedIdentity();
        if (!selectedIdentityResult.IsSuccess)
        {
            return Task.FromResult(AuthenticateResult.Fail(selectedIdentityResult.ErrorMessage!));
        }

        var member = selectedIdentityResult.Identity!;

        return Task.FromResult(BuildResult(
        [
            new Claim("tid", TestMemberIdentities.TenantId.ToString()),
            new Claim("oid", member.EntraObjectId.ToString()),
            new Claim(PlanDeckIdentity.AppUserIdClaim, member.AppUserId.ToString()),
            new Claim(PlanDeckIdentity.ActiveUserClaim, bool.TrueString),
            new Claim("name", member.DisplayName),
            new Claim("email", member.Email)
        ]));
    }

    private (bool IsSuccess, TestMemberIdentity? Identity, string? ErrorMessage) ResolveSelectedIdentity()
    {
        if (!Request.Cookies.TryGetValue(UserSelectionCookie, out var selection)
            || string.IsNullOrWhiteSpace(selection))
        {
            return (true, TestMemberIdentities.Owner, null);
        }

        var selected = TestMemberIdentities.All.SingleOrDefault(
            identity => string.Equals(
                identity.SelectionKey,
                selection,
                StringComparison.OrdinalIgnoreCase));

        if (selected is null)
        {
            return (
                false,
                null,
                $"Unknown deterministic test identity '{selection}'.");
        }

        return (true, selected, null);
    }

    private static AuthenticateResult BuildResult(Claim[] claims)
    {
        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        return AuthenticateResult.Success(ticket);
    }
}
