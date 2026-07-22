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

        var sessions = new SessionsPage(Page, AspireAppFixture.BaseUrl);
        var members = new SessionMembersPage(Page);
        var projectName = await new ProjectsPage(Page, AspireAppFixture.BaseUrl)
            .CreateUniqueProjectAsync("E2E Members Project");

        await sessions.GotoAsync();
        await sessions.CreateSessionAsync(sessionName, taskTitle, projectName);

        // Creating a session auto-selects it, revealing the Members section.
        await members.AssignMemberAsync(email);
        await Expect(members.MemberEntry(email)).ToBeVisibleAsync(new() { Timeout = 15_000 });

        await members.RemoveMemberAsync(email);
        await Expect(members.MemberEntry(email)).Not.ToBeVisibleAsync(new() { Timeout = 15_000 });
    }
}
