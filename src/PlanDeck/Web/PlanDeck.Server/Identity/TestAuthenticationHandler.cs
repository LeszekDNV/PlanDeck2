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

    private const string TestTenantId = "11111111-1111-1111-1111-111111111111";
    private const string TestObjectId = "22222222-2222-2222-2222-222222222222";
    private const string TestDisplayName = "Test User";
    private const string TestEmail = "test.user@plandeck.local";

    private const string SecondObjectId = "33333333-3333-3333-3333-333333333333";
    private const string SecondDisplayName = "Test User B";
    private const string SecondEmail = "test.userb@plandeck.local";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var isSecondUser =
            Request.Cookies.TryGetValue(UserSelectionCookie, out var selection)
            && string.Equals(selection, "b", StringComparison.OrdinalIgnoreCase);

        var objectId = isSecondUser ? SecondObjectId : TestObjectId;
        var displayName = isSecondUser ? SecondDisplayName : TestDisplayName;
        var email = isSecondUser ? SecondEmail : TestEmail;

        var claims = new[]
        {
            new Claim("tid", TestTenantId),
            new Claim("oid", objectId),
            new Claim("name", displayName),
            new Claim("email", email)
        };

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
