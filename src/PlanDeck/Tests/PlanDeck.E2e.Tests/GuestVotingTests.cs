using Microsoft.Playwright;
using PlanDeck.E2e.Tests.Pages;

namespace PlanDeck.E2e.Tests;

[TestFixture]
public class GuestVotingTests : PageTest
{
    public override BrowserNewContextOptions ContextOptions() => new()
    {
        IgnoreHTTPSErrors = true
    };

    [Test]
    public async Task Join_WithUnknownCode_ShowsError_AndStaysOnJoinPage()
    {
        var join = new JoinSessionPage(Page, AspireAppFixture.BaseUrl);
        await join.GotoAsync("NOSUCHCODE9");

        await join.SubmitNameAsync("Ghost");

        await Expect(join.ErrorAlert).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Expect(Page).ToHaveURLAsync(new Regex("/join/NOSUCHCODE9$"));
    }
}
