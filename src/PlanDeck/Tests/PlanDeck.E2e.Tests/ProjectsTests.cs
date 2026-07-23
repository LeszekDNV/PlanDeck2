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
        var runId = Guid.NewGuid();
        var teamName = $"Shared Team {Guid.NewGuid():N}";
        var sessionName = $"e2e-scenario-session-{runId:N}";
        const string memberEmail = "test.member@plandeck.local";
        const string adminEmail = "test.admin@plandeck.local";
        const string deleteWarning = "Delete this project and all its sessions, tasks, participants, memberships, team links, and Azure DevOps configuration? This cannot be undone.";
        var scenarioClient = E2eScenarioClient.Create(
            AspireAppFixture.BaseUrl,
            AspireAppFixture.E2eScenarioToken);
        var scenario = await scenarioClient.SeedAsync(
            runId,
            E2eScenarioSessionStatus.Draft,
            taskCount: 1);

        try
        {
            var teams = new TeamsPage(Page, AspireAppFixture.BaseUrl);
            await teams.GotoAsync();
            await teams.CreateTeamAsync(teamName);

            var details = new ProjectDetailsPage(Page, AspireAppFixture.BaseUrl);
            await details.GotoAsync(scenario.ProjectId);
            await details.AssignTeamAsync(teamName);
            await details.OpenSessionsAsync();

            var sessions = new SessionsPage(Page, AspireAppFixture.BaseUrl);
            await sessions.SelectSessionAsync(sessionName);

            var sessionMembers = new SessionMembersPage(Page);
            await sessionMembers.AssignMemberAsync(memberEmail);

            await sessions.ActivateAsync();
            var votingSessionId = await sessions.JoinVotingAsync();
            await sessions.GotoAsync(scenario.ProjectId);
            await sessions.CreateSessionAsync($"inactive-session-{runId:N}", $"inactive-task-{runId:N}");

            await details.GotoAsync(scenario.ProjectId);
            await details.OpenDeleteProjectDialogAsync();
            await Expect(details.DeleteDialog.GetByText(deleteWarning, new() { Exact = true }))
                .ToBeVisibleAsync();
            await details.ConfirmDeleteProjectAsync();

            await Expect(Page).ToHaveURLAsync(new Regex("/projects$"), new() { Timeout = 15_000 });

            await Page.GotoAsync($"{AspireAppFixture.BaseUrl.TrimEnd('/')}/projects/{scenario.ProjectId:D}", new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 120_000 });
            await Expect(Page.GetByText("The selected project no longer exists or is not accessible.")).ToBeVisibleAsync(new() { Timeout = 15_000 });

            await Page.GotoAsync($"{AspireAppFixture.BaseUrl.TrimEnd('/')}/projects/{scenario.ProjectId:D}/sessions", new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 120_000 });
            await Expect(Page.GetByText("The selected project no longer exists or is not accessible.")).ToBeVisibleAsync(new() { Timeout = 15_000 });

            await Page.GotoAsync($"{AspireAppFixture.BaseUrl.TrimEnd('/')}/voting/{votingSessionId:D}", new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 120_000 });
            await Expect(Page.GetByText("The session could not be loaded.", new() { Exact = true }))
                .ToBeVisibleAsync(new() { Timeout = 15_000 });

            await teams.GotoAsync();
            await teams.SelectTeamAsync(teamName);
            await teams.AddMemberAsync(adminEmail);
            await Expect(teams.MemberEntry(adminEmail)).ToBeVisibleAsync(new() { Timeout = 15_000 });
            await teams.RemoveMemberAsync(adminEmail);
        }
        finally
        {
            await scenarioClient.CleanupAsync(runId);
        }
    }
}
