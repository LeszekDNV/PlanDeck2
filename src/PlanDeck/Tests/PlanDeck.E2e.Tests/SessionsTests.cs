using Microsoft.Playwright;
using PlanDeck.E2e.Tests.Pages;

namespace PlanDeck.E2e.Tests;

[TestFixture]
public class SessionsTests : PageTest
{
    public override BrowserNewContextOptions ContextOptions() => new()
    {
        IgnoreHTTPSErrors = true
    };

    [Test]
    public async Task CreateSession_WithAdHocTask_RendersInList()
    {
        var sessionName = $"E2E Session {Guid.NewGuid():N}";
        var taskTitle = $"E2E Task {Guid.NewGuid():N}";

        var sessions = new SessionsPage(Page, AspireAppFixture.BaseUrl);

        await sessions.GotoAsync();
        await sessions.CreateSessionAsync(sessionName, taskTitle);

        await Expect(sessions.SessionEntry(sessionName)).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Expect(sessions.TaskEntry(taskTitle)).ToBeVisibleAsync(new() { Timeout = 15_000 });
    }
}
