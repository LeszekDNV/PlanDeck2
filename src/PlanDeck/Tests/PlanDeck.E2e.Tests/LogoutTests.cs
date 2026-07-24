using Microsoft.Playwright;
using PlanDeck.E2e.Tests.Pages;

namespace PlanDeck.E2e.Tests;

[TestFixture]
public sealed class LogoutTests : PageTest
{
    public override BrowserNewContextOptions ContextOptions() => new()
    {
        IgnoreHTTPSErrors = true
    };

    [Test]
    public async Task LocalAccountLogout_EndsSessionUntilNextLogin()
    {
        var credentials = await LocalAccountFlow.RegisterConfirmAndLoginAsync(Page, AspireAppFixture.BaseUrl);

        var layout = new MainLayoutPage(Page, AspireAppFixture.BaseUrl);
        await layout.OpenAuthenticatedApplicationAsync();

        await layout.LogOutAsync();
        await Expect(layout.LogInButton).ToBeVisibleAsync();

        await layout.ReloadAnonymousApplicationAsync();
        await Expect(layout.LogInButton).ToBeVisibleAsync();

        await LocalAccountFlow.LoginAsync(Page, AspireAppFixture.BaseUrl, credentials);
        await Expect(layout.LogOutButton).ToBeVisibleAsync();
    }
}
