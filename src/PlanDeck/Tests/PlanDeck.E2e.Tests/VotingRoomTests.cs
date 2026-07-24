using Microsoft.Playwright;
using PlanDeck.E2e.Tests.Pages;

namespace PlanDeck.E2e.Tests;

[TestFixture]
public class VotingRoomTests : PageTest
{
    public override BrowserNewContextOptions ContextOptions() => new()
    {
        IgnoreHTTPSErrors = true
    };

    [Test]
    public async Task Owner_VoteRevealPick_Persists()
    {
        await LocalAccountFlow.RegisterConfirmAndLoginAsync(Page, AspireAppFixture.BaseUrl);

        var sessionName = $"E2E Voting {Guid.NewGuid():N}";
        var taskTitle = $"E2E Task {Guid.NewGuid():N}";

        var projectId = await CreateProjectAndGetIdAsync("E2E Voting Project");

        var sessions = new SessionsPage(Page, AspireAppFixture.BaseUrl);
        await sessions.GotoAsync(projectId);
        await sessions.CreateSessionAsync(sessionName, taskTitle);
        await sessions.ActivateAsync();

        var sessionId = await sessions.JoinVotingAsync();
        var voting = new VotingRoomPage(Page, AspireAppFixture.BaseUrl);
        await voting.WaitForLoadedAsync();

        await voting.SelectTaskAsync(taskTitle);
        await voting.VoteAsync("5");
        await voting.RevealAsync();
        await voting.PickEstimateAsync("5");
        await Expect(voting.AgreedEstimate).ToContainTextAsync("5", new() { Timeout = 15_000 });

        await voting.GotoAsync(sessionId);
        await voting.SelectTaskAsync(taskTitle);
        await Expect(voting.AgreedEstimate).ToContainTextAsync("5", new() { Timeout = 15_000 });
    }

    private async Task<Guid> CreateProjectAndGetIdAsync(string prefix)
    {
        var projects = new ProjectsPage(Page, AspireAppFixture.BaseUrl);
        return await projects.CreateProjectReturningIdAsync(prefix);
    }
}
