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
        await LocalAccountFlow.RegisterConfirmAndLoginAsync(Page, AspireAppFixture.BaseUrl);

        var runId = Guid.NewGuid();
        var teamName = $"Shared Team {Guid.NewGuid():N}";
        var sessionName = $"e2e-session-{runId:N}";
        var taskName = $"e2e-task-{runId:N}";
        const string memberEmail = "member@example.com";
        const string adminEmail = "admin@example.com";
        const string deleteWarning = "Delete this project and all its sessions, tasks, participants, memberships, team links, and Azure DevOps configuration? This cannot be undone.";

        var projects = new ProjectsPage(Page, AspireAppFixture.BaseUrl);
        var projectId = await projects.CreateProjectReturningIdAsync("E2E Project Delete");

        var teams = new TeamsPage(Page, AspireAppFixture.BaseUrl);
        await teams.GotoAsync();
        await teams.CreateTeamAsync(teamName);

        var details = new ProjectDetailsPage(Page, AspireAppFixture.BaseUrl);
        await details.GotoAsync(projectId);
        await details.AssignTeamAsync(teamName);
        await details.OpenSessionsAsync();

        var sessions = new SessionsPage(Page, AspireAppFixture.BaseUrl);
        await sessions.CreateSessionAsync(sessionName, taskName);
        var members = new SessionMembersPage(Page);
        await members.AssignMemberAsync(memberEmail);

        await details.GotoAsync(projectId);
        await details.OpenDeleteProjectDialogAsync();
        await Expect(details.DeleteDialog.GetByText(deleteWarning, new() { Exact = true })).ToBeVisibleAsync();
        await details.ConfirmDeleteProjectAsync();

        await Expect(Page).ToHaveURLAsync(new Regex("/projects$"), new() { Timeout = 15_000 });

        await teams.GotoAsync();
        await teams.SelectTeamAsync(teamName);
        await teams.AddMemberAsync(adminEmail);
        await Expect(teams.MemberEntry(adminEmail)).ToBeVisibleAsync(new() { Timeout = 15_000 });
    }
}


