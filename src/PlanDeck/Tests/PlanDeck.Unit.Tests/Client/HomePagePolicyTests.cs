using System.Security.Claims;
using PlanDeck.Client.Pages;

namespace PlanDeck.Unit.Tests.Client;

[TestFixture]
public class HomePagePolicyTests
{
    [Test]
    public void GetView_UnauthenticatedPrincipal_ReturnsAnonymous()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity());

        var result = HomePagePolicy.GetView(user);

        Assert.That(result, Is.EqualTo(HomePageView.Anonymous));
    }

    [Test]
    public void GetView_AuthenticatedPrincipal_ReturnsRegistered()
    {
        var user = CreateAuthenticatedPrincipal();

        var result = HomePagePolicy.GetView(user);

        Assert.That(result, Is.EqualTo(HomePageView.Registered));
    }

    [TestCase("true")]
    [TestCase("TRUE")]
    [TestCase("True")]
    public void GetView_AuthenticatedGuestClaim_ReturnsGuest(string claimValue)
    {
        var user = CreateAuthenticatedPrincipal(new Claim("is_guest", claimValue));

        var result = HomePagePolicy.GetView(user);

        Assert.That(result, Is.EqualTo(HomePageView.Guest));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void BuildJoinRoute_EmptyCode_ReturnsNull(string? code)
    {
        var result = HomePagePolicy.BuildJoinRoute(code);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void BuildJoinRoute_CodeWithWhitespace_TrimsCode()
    {
        var result = HomePagePolicy.BuildJoinRoute("  ABC123  ");

        Assert.That(result, Is.EqualTo("/join/ABC123"));
    }

    [Test]
    public void BuildJoinRoute_CodeWithReservedCharacters_EncodesRouteSegment()
    {
        var result = HomePagePolicy.BuildJoinRoute("team/alpha 1");

        Assert.That(result, Is.EqualTo("/join/team%2Falpha%201"));
    }

    private static ClaimsPrincipal CreateAuthenticatedPrincipal(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, "Test"));
}
