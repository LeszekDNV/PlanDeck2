using Microsoft.Playwright;
using PlanDeck.E2e.Tests.Pages;

namespace PlanDeck.E2e.Tests;

[TestFixture]
public class ProjectsTests : PageTest
{
    public override BrowserNewContextOptions ContextOptions() => new()
    {
        IgnoreHTTPSErrors = true
    };

    [Test]
    public async Task DeletingProjectWithSession_RemovesProjectAndKeepsSharedTeam()
    {
        var teamName = $"Shared Team {Guid.NewGuid():N}";
        var sessionName = $"Project Delete Session {Guid.NewGuid():N}";
        var taskTitle = $"Task {Guid.NewGuid():N}";

        var teams = new TeamsPage(Page, AspireAppFixture.BaseUrl);
        await teams.GotoAsync();
        await teams.CreateTeamAsync(teamName);

        var projects = new ProjectsPage(Page, AspireAppFixture.BaseUrl);
        await projects.GotoAsync();
        var projectName = await projects.CreateUniqueProjectAsync("Cascade Delete Project");
        await projects.OpenProjectAsync(projectName);

        var projectId = ParseLastUrlGuid(Page.Url);
        var details = new ProjectDetailsPage(Page, AspireAppFixture.BaseUrl);

        await details.AssignTeamAsync(teamName);
        await details.OpenSessionsAsync();

        var sessions = new SessionsPage(Page, AspireAppFixture.BaseUrl);
        await sessions.CreateSessionAsync(sessionName, taskTitle);

        await Page.GotoAsync($"{AspireAppFixture.BaseUrl.TrimEnd('/')}/projects/{projectId:D}", new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 120_000 });
        await details.DeleteProjectAsync();

        await Expect(Page).ToHaveURLAsync(new Regex("/projects$"), new() { Timeout = 15_000 });

        await Page.GotoAsync($"{AspireAppFixture.BaseUrl.TrimEnd('/')}/projects/{projectId:D}", new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 120_000 });
        await Expect(Page.GetByText("The selected project no longer exists or is not accessible.")).ToBeVisibleAsync(new() { Timeout = 15_000 });

        await Page.GotoAsync($"{AspireAppFixture.BaseUrl.TrimEnd('/')}/projects/{projectId:D}/sessions", new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 120_000 });
        await Expect(Page.GetByText("The selected project no longer exists or is not accessible.")).ToBeVisibleAsync(new() { Timeout = 15_000 });

        await teams.GotoAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = teamName, Exact = true })).ToBeVisibleAsync(new() { Timeout = 15_000 });
    }

    private static Guid ParseLastUrlGuid(string url)
    {
        var uri = new Uri(url, UriKind.Absolute);
        var segment = uri.Segments.Last().Trim('/');
        return Guid.Parse(segment);
    }
}


