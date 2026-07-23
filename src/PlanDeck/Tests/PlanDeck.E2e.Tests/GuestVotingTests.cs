using System.Text.RegularExpressions;
using Microsoft.Playwright;
using PlanDeck.E2e.Tests.Pages;

namespace PlanDeck.E2e.Tests;

[TestFixture]
public class GuestVotingTests : PageTest
{
    public override BrowserNewContextOptions ContextOptions() => new()
    {
        IgnoreHTTPSErrors = true
    };

    [Test]
    public async Task Guest_JoinsActiveSession_Votes_AndSeesReveal_WithoutControls()
    {
        var sessionName = $"E2E Guest {Guid.NewGuid():N}";
        var taskTitle = $"E2E Task {Guid.NewGuid():N}";

        var projectId = await CreateProjectAndGetIdAsync("E2E Guest Project");
        var sessionsA = new SessionsPage(Page, AspireAppFixture.BaseUrl);

        await sessionsA.GotoAsync(projectId);
        await sessionsA.CreateSessionAsync(sessionName, taskTitle);
        await sessionsA.ActivateAsync();

        var sessionId = await sessionsA.JoinVotingAsync();
        var votingA = new VotingRoomPage(Page, AspireAppFixture.BaseUrl);
        await votingA.WaitForLoadedAsync();

        await using var guestContext = await E2eIdentityContextFactory.CreateGuestContextAsync(
            Browser,
            AspireAppFixture.BaseUrl,
            sessionId,
            ContextOptions());

        var guestPage = await guestContext.NewPageAsync();
        var votingGuest = new VotingRoomPage(guestPage, AspireAppFixture.BaseUrl);
        await votingGuest.GotoAsync(sessionId);

        await Expect(votingA.Participants).ToHaveCountAsync(2, new() { Timeout = 15_000 });
        await Expect(votingGuest.Participants).ToHaveCountAsync(2, new() { Timeout = 15_000 });

        await Expect(votingGuest.RevealButton).ToHaveCountAsync(0);
        await Expect(votingGuest.ResetButton).ToHaveCountAsync(0);

        await votingA.SelectTaskAsync(taskTitle);
        await Expect(votingGuest.VoteButton("5")).ToBeEnabledAsync(new() { Timeout = 15_000 });
        await votingGuest.VoteAsync("5");

        await Expect(votingGuest.RevealedVotes).ToHaveCountAsync(0);
        await votingA.RevealAsync();

        await Expect(votingA.RevealedVotes.Filter(new() { HasText = "5" }))
            .ToHaveCountAsync(1, new() { Timeout = 15_000 });
        await Expect(votingGuest.RevealedVotes.Filter(new() { HasText = "5" }))
            .ToHaveCountAsync(1, new() { Timeout = 15_000 });
    }

    [Test]
    public async Task Join_WithActiveCode_NavigatesToVotingPage()
    {
        var runId = Guid.NewGuid();
        var shareCode = runId.ToString("N")[..12].ToUpperInvariant();
        var scenarioClient = E2eScenarioClient.Create(
            AspireAppFixture.BaseUrl,
            AspireAppFixture.E2eScenarioToken);
        var scenario = await scenarioClient.SeedAsync(
            runId,
            E2eScenarioSessionStatus.Active,
            taskCount: 1);

        try
        {
            var join = new JoinSessionPage(Page, AspireAppFixture.BaseUrl);
            await join.GotoAsync(shareCode);
            await join.SubmitNameAsync($"Guest {runId:N}");

            await Expect(Page).ToHaveURLAsync(
                new Regex($"/voting/{scenario.SessionId:D}$"),
                new() { Timeout = 15_000 });
        }
        finally
        {
            await scenarioClient.CleanupAsync(runId);
        }
    }

    [Test]
    public async Task Join_WithUnknownCode_ShowsError_AndStaysOnJoinPage()
    {
        var join = new JoinSessionPage(Page, AspireAppFixture.BaseUrl);
        await join.GotoAsync("NOSUCHCODE9");

        await join.SubmitNameAsync("Ghost");

        await Expect(join.ErrorAlert).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Expect(Page).ToHaveURLAsync(new Regex("/join/NOSUCHCODE9$"));
    }

    private async Task<Guid> CreateProjectAndGetIdAsync(string prefix)
    {
        var projects = new ProjectsPage(Page, AspireAppFixture.BaseUrl);
        return await projects.CreateProjectReturningIdAsync(prefix);
    }
}
