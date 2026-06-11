using Microsoft.Playwright;
using PlanDeck.E2e.Tests.Pages;

namespace PlanDeck.E2e.Tests;

[TestFixture]
public class HomePageTests : PageTest
{
    public override BrowserNewContextOptions ContextOptions() => new()
    {
        IgnoreHTTPSErrors = true
    };

    [Test]
    public async Task CallServerButton_ReturnsHelloWorld()
    {
        var home = new HomePage(Page, AspireAppFixture.BaseUrl);

        await home.GotoAsync();
        await home.ClickCallServerAsync();

        await Expect(home.ServerResponse).ToBeVisibleAsync(new() { Timeout = 15_000 });
    }
}
