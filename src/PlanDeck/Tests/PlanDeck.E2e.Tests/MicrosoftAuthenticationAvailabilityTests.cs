using Microsoft.Playwright;
using PlanDeck.E2e.Tests.Pages;

namespace PlanDeck.E2e.Tests;

// Risk F4: available Microsoft authentication must surface every related account action.
[TestFixture]
public sealed class MicrosoftAuthenticationAvailabilityTests : PageTest
{
    public override BrowserNewContextOptions ContextOptions() => new()
    {
        IgnoreHTTPSErrors = true
    };

    [Test]
    public async Task AvailableMicrosoftAuthentication_ShowsActionsAcrossAccountPages()
    {
        var loginPage = new LoginPage(Page, AspireAppFixture.BaseUrl);
        await loginPage.GotoAsync();
        await Expect(loginPage.MicrosoftButton).ToBeVisibleAsync();

        var registerPage = new RegisterPage(Page, AspireAppFixture.BaseUrl);
        await registerPage.GotoAsync();
        await Expect(registerPage.MicrosoftButton).ToBeVisibleAsync();

        var credentials = await LocalAccountFlow.RegisterConfirmAndLoginAsync(
            Page,
            AspireAppFixture.BaseUrl);
        var securityPage = new AccountSecurityPage(Page, AspireAppFixture.BaseUrl);
        await securityPage.GotoAsync();
        await Expect(securityPage.LinkMicrosoftButton).ToBeVisibleAsync();

        await securityPage.StartLinkMicrosoftAsync(credentials.Password);
        await Expect(securityPage.PasswordInput).ToBeVisibleAsync();
    }
}
