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
    public async Task ProjectFirstSession_CreatesSessionVisibleInProjectRoute()
    {
        var sessionName = $"E2E Session {Guid.NewGuid():N}";
        var taskTitle = $"E2E Task {Guid.NewGuid():N}";

        var projects = new ProjectsPage(Page, AspireAppFixture.BaseUrl);
        await projects.GotoAsync();
        var projectName = await projects.CreateUniqueProjectAsync("E2E Project First");
        await projects.OpenProjectAsync(projectName);

        var projectId = ParseLastUrlGuid(Page.Url);
        var sessions = new SessionsPage(Page, AspireAppFixture.BaseUrl);
        await sessions.GotoAsync(projectId);
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

        var projectId = await CreateProjectAndGetIdAsync("E2E Edit Project");
        var sessions = new SessionsPage(Page, AspireAppFixture.BaseUrl);

        await sessions.GotoAsync(projectId);
        await sessions.CreateSessionAsync(sessionName, original);

        await sessions.EditTaskAsync(original, renamed, "A **bold** detail.");

        await Expect(sessions.ConfigTask(renamed)).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Expect(sessions.ConfigTask(renamed).Locator("strong")).ToHaveTextAsync("bold", new() { Timeout = 15_000 });
    }

    [Test]
    public async Task RootRedirectsToProjects_AndLegacySessionsRouteIsNotFound()
    {
        await Page.GotoAsync(AspireAppFixture.BaseUrl, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 120_000 });
        await Expect(Page).ToHaveURLAsync(new Regex("/projects$"), new() { Timeout = 15_000 });

        await Page.GotoAsync($"{AspireAppFixture.BaseUrl.TrimEnd('/')}/sessions", new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 120_000 });
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "404 - Page Not Found", Exact = true }))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });
    }

    [Test]
    public async Task Sessions_RendersOnMobileViewport()
    {
        var projectId = await CreateProjectAndGetIdAsync("E2E Mobile Project");
        var sessions = new SessionsPage(Page, AspireAppFixture.BaseUrl);

        await Page.SetViewportSizeAsync(390, 844);
        await sessions.GotoAsync(projectId);

        await Expect(sessions.CreateSessionButton).ToBeVisibleAsync(new() { Timeout = 15_000 });
    }

    private async Task<Guid> CreateProjectAndGetIdAsync(string prefix)
    {
        var projects = new ProjectsPage(Page, AspireAppFixture.BaseUrl);
        await projects.GotoAsync();
        var projectName = await projects.CreateUniqueProjectAsync(prefix);
        await projects.OpenProjectAsync(projectName);
        return ParseLastUrlGuid(Page.Url);
    }

    private static Guid ParseLastUrlGuid(string url)
    {
        var uri = new Uri(url, UriKind.Absolute);
        var segment = uri.Segments.Last().Trim('/');
        return Guid.Parse(segment);
    }
}



