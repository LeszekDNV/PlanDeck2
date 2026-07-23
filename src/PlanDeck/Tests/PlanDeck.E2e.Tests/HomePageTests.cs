using Microsoft.Playwright;

namespace PlanDeck.E2e.Tests;

[TestFixture]
public class HomePageTests : PageTest
{
    public override BrowserNewContextOptions ContextOptions() => new()
    {
        IgnoreHTTPSErrors = true
    };

    [Test]
    public async Task Home_RedirectsAuthenticatedUserToProjects()
    {
        await Page.GotoAsync(AspireAppFixture.BaseUrl, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 120_000 });
        await Expect(Page).ToHaveURLAsync(new Regex("/projects$"), new() { Timeout = 15_000 });
    }
}


