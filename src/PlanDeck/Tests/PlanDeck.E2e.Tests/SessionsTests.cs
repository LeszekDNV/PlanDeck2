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

    [Test]
    public async Task EditTask_UpdatesTitleAndRendersMarkdownDescription()
    {
        var sessionName = $"E2E Edit {Guid.NewGuid():N}";
        var original = $"Original {Guid.NewGuid():N}";
        var renamed = $"Renamed {Guid.NewGuid():N}";

        var sessions = new SessionsPage(Page, AspireAppFixture.BaseUrl);

        await sessions.GotoAsync();
        await sessions.CreateSessionAsync(sessionName, original);

        await sessions.EditTaskAsync(original, renamed, "A **bold** detail.");

        // Title updated and Markdown renders as a <strong> element (display-only).
        await Expect(sessions.ConfigTask(renamed)).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Expect(sessions.ConfigTask(renamed).Locator("strong")).ToHaveTextAsync("bold", new() { Timeout = 15_000 });
    }

    [Test]
    public async Task BulkPaste_AddsMultipleTasksWithDescription()
    {
        var sessionName = $"E2E Bulk {Guid.NewGuid():N}";
        var marker = Guid.NewGuid().ToString("N");
        var login = $"Login {marker}";
        var logout = $"Logout {marker}";
        var dashboard = $"Dashboard {marker}";

        var bulk = $"{login} | A **bold** login screen.\n{logout}\n{dashboard} | Overview widgets";

        var sessions = new SessionsPage(Page, AspireAppFixture.BaseUrl);

        await sessions.GotoAsync();
        await sessions.CreateSessionWithBulkAsync(sessionName, bulk);

        await Expect(sessions.ConfigTask(login)).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Expect(sessions.ConfigTask(logout)).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Expect(sessions.ConfigTask(dashboard)).ToBeVisibleAsync(new() { Timeout = 15_000 });

        // The piped description is parsed and rendered as Markdown.
        await Expect(sessions.ConfigTask(login).Locator("strong")).ToHaveTextAsync("bold", new() { Timeout = 15_000 });
    }

    [Test]
    public async Task Sessions_RendersOnMobileViewport()
    {
        var sessions = new SessionsPage(Page, AspireAppFixture.BaseUrl);

        await Page.SetViewportSizeAsync(390, 844);
        await sessions.GotoAsync();

        // Core entry point stays reachable in the single-column mobile layout.
        await Expect(sessions.CreateSessionButton).ToBeVisibleAsync(new() { Timeout = 15_000 });
    }
}
