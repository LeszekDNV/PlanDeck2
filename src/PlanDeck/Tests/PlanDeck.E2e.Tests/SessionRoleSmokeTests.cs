using Microsoft.Playwright;
using PlanDeck.E2e.Tests.Pages;

namespace PlanDeck.E2e.Tests;

[TestFixture]
public class SessionRoleSmokeTests : PageTest
{
    public override BrowserNewContextOptions ContextOptions() => new()
    {
        IgnoreHTTPSErrors = true
    };

    [Test]
    public async Task OwnerCanCreateAndActivateSession()
    {
        await LocalAccountFlow.RegisterConfirmAndLoginAsync(Page, AspireAppFixture.BaseUrl);

        var sessionName = $"owner-created-{Guid.NewGuid():N}";
        var taskTitle = $"owner-task-{Guid.NewGuid():N}";

        var projects = new ProjectsPage(Page, AspireAppFixture.BaseUrl);
        var projectId = await projects.CreateProjectReturningIdAsync("E2E Owner Role");

        var sessions = new SessionsPage(Page, AspireAppFixture.BaseUrl);
        await sessions.GotoAsync(projectId);
        await sessions.CreateSessionAsync(sessionName, taskTitle);
        await sessions.ActivateAsync();

        await Expect(sessions.SessionEntry(sessionName)).ToBeVisibleAsync(new() { Timeout = 15_000 });
    }
}


