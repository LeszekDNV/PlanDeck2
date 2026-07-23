using Microsoft.Playwright;
using PlanDeck.E2e.Tests.Pages;

namespace PlanDeck.E2e.Tests;

[TestFixture]
public class SessionMembersTests : PageTest
{
    public override BrowserNewContextOptions ContextOptions() => new()
    {
        IgnoreHTTPSErrors = true
    };

    [Test]
    public async Task AssignAndRemoveMember_UpdatesMemberList()
    {
        var sessionName = $"E2E Members {Guid.NewGuid():N}";
        var taskTitle = $"E2E Task {Guid.NewGuid():N}";
        var email = $"member-{Guid.NewGuid():N}@example.com";

        var projectId = await CreateProjectAndGetIdAsync("E2E Members Project");

        var sessions = new SessionsPage(Page, AspireAppFixture.BaseUrl);
        var members = new SessionMembersPage(Page);

        await sessions.GotoAsync(projectId);
        await sessions.CreateSessionAsync(sessionName, taskTitle);

        await members.AssignMemberAsync(email);
        await Expect(members.MemberEntry(email)).ToBeVisibleAsync(new() { Timeout = 15_000 });

        await members.RemoveMemberAsync(email);
        await Expect(members.MemberEntry(email)).Not.ToBeVisibleAsync(new() { Timeout = 15_000 });
    }

    private async Task<Guid> CreateProjectAndGetIdAsync(string prefix)
    {
        var projects = new ProjectsPage(Page, AspireAppFixture.BaseUrl);
        await projects.GotoAsync();
        var projectName = await projects.CreateUniqueProjectAsync(prefix);
        await projects.OpenProjectAsync(projectName);

        var uri = new Uri(Page.Url, UriKind.Absolute);
        return Guid.Parse(uri.Segments.Last().Trim('/'));
    }
}
