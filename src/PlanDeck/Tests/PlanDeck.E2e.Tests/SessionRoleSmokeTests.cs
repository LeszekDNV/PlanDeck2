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
    public async Task OwnerAndAdminCanMutate_MemberIsReadOnly()
    {
        var runId = Guid.NewGuid();
        var scenarioClient = E2eScenarioClient.Create(AspireAppFixture.BaseUrl, AspireAppFixture.E2eScenarioToken);
        var scenario = await scenarioClient.SeedAsync(runId, E2eScenarioSessionStatus.Draft, taskCount: 1);

        try
        {
            var ownerSessionName = $"owner-created-{runId:N}";
            await using (var ownerContext = await E2eIdentityContextFactory.CreateOwnerContextAsync(
                             Browser,
                             AspireAppFixture.BaseUrl,
                             ContextOptions()))
            {
                var ownerPage = await ownerContext.NewPageAsync();
                var ownerSessions = new SessionsPage(ownerPage, AspireAppFixture.BaseUrl);

                await ownerSessions.GotoAsync(scenario.ProjectId);
                await ownerSessions.CreateSessionAsync(ownerSessionName, $"owner-task-{runId:N}");
                await Expect(ownerSessions.SessionEntry(ownerSessionName)).ToBeVisibleAsync(new() { Timeout = 15_000 });
            }

            var adminSessionName = $"admin-created-{runId:N}";
            await using (var adminContext = await E2eIdentityContextFactory.CreateAdminContextAsync(
                             Browser,
                             AspireAppFixture.BaseUrl,
                             ContextOptions()))
            {
                var adminPage = await adminContext.NewPageAsync();
                var adminSessions = new SessionsPage(adminPage, AspireAppFixture.BaseUrl);

                await adminSessions.GotoAsync(scenario.ProjectId);
                await adminSessions.CreateSessionAsync(adminSessionName, $"admin-task-{runId:N}");
                await Expect(adminSessions.SessionEntry(adminSessionName)).ToBeVisibleAsync(new() { Timeout = 15_000 });
            }

            await using (var memberContext = await E2eIdentityContextFactory.CreateMemberContextAsync(
                             Browser,
                             AspireAppFixture.BaseUrl,
                             ContextOptions()))
            {
                var memberPage = await memberContext.NewPageAsync();
                var memberSessions = new SessionsPage(memberPage, AspireAppFixture.BaseUrl);

                await memberSessions.GotoAsync(scenario.ProjectId);
                await memberSessions.SelectSessionAsync($"e2e-scenario-session-{runId:N}");

                await Expect(memberSessions.CreateSessionButton).ToHaveCountAsync(0);
                await Expect(memberSessions.SaveConfigurationButton).ToHaveCountAsync(0);
                await Expect(memberSessions.AssignMemberButton).ToHaveCountAsync(0);
            }
        }
        finally
        {
            await scenarioClient.CleanupAsync(runId);
        }
    }
}
