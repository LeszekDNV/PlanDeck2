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
    public async Task TestingLogout_RemainsAnonymousAfterReload_AndLoginRestoresTestOwner()
    {
        var layout = new MainLayoutPage(Page, AspireAppFixture.BaseUrl);

        await layout.OpenAuthenticatedApplicationAsync();
        await Expect(layout.TestOwner).ToBeVisibleAsync();

        await layout.LogOutAsync();
        await Expect(layout.LogInButton).ToBeVisibleAsync();

        await layout.ReloadAnonymousApplicationAsync();
        await Expect(layout.LogInButton).ToBeVisibleAsync();

        await layout.LogInAsync();
        await Expect(layout.TestOwner).ToBeVisibleAsync();
    }
}
