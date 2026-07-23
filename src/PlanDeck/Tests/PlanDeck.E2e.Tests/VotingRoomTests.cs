using Microsoft.Playwright;
using PlanDeck.E2e.Tests.Pages;

namespace PlanDeck.E2e.Tests;

[TestFixture]
public class VotingRoomTests : PageTest
{
    private const string MemberEmail = "test.member@plandeck.local";

    public override BrowserNewContextOptions ContextOptions() => new()
    {
        IgnoreHTTPSErrors = true
    };

    [Test]
    public async Task TwoMembers_VoteRevealPick_SyncsLiveAndPersists()
    {
        var sessionName = $"E2E Voting {Guid.NewGuid():N}";
        var taskTitle = $"E2E Task {Guid.NewGuid():N}";

        var projectId = await CreateProjectAndGetIdAsync("E2E Voting Project");

        var sessionsA = new SessionsPage(Page, AspireAppFixture.BaseUrl);
        var membersA = new SessionMembersPage(Page);

        await sessionsA.GotoAsync(projectId);
        await sessionsA.CreateSessionAsync(sessionName, taskTitle);
        await membersA.AssignMemberAsync(MemberEmail);
        await sessionsA.ActivateAsync();

        var sessionId = await sessionsA.JoinVotingAsync();
        var votingA = new VotingRoomPage(Page, AspireAppFixture.BaseUrl);
        await votingA.WaitForLoadedAsync();

        await using var contextB = await E2eIdentityContextFactory.CreateMemberContextAsync(
            Browser,
            AspireAppFixture.BaseUrl,
            ContextOptions());

        var pageB = await contextB.NewPageAsync();
        var votingB = new VotingRoomPage(pageB, AspireAppFixture.BaseUrl);
        await votingB.GotoAsync(sessionId);

        await Expect(votingA.Participants).ToHaveCountAsync(2, new() { Timeout = 15_000 });
        await Expect(votingB.Participants).ToHaveCountAsync(2, new() { Timeout = 15_000 });

        await votingA.SelectTaskAsync(taskTitle);
        await Expect(votingB.VoteButton("3")).ToBeEnabledAsync(new() { Timeout = 15_000 });

        await votingA.VoteAsync("3");
        await votingB.VoteAsync("5");

        await Expect(votingA.VotedStatuses).ToHaveCountAsync(2, new() { Timeout = 15_000 });
        await Expect(votingA.RevealedVotes).ToHaveCountAsync(0);

        await votingA.RevealAsync();
        await Expect(votingA.RevealedVotes).ToHaveCountAsync(2, new() { Timeout = 15_000 });
        await Expect(votingB.RevealedVotes).ToHaveCountAsync(2, new() { Timeout = 15_000 });

        await votingB.PickEstimateAsync("5");
        await Expect(votingA.AgreedEstimate).ToContainTextAsync("5", new() { Timeout = 15_000 });

        await votingB.GotoAsync(sessionId);
        await votingB.SelectTaskAsync(taskTitle);
        await Expect(votingB.AgreedEstimate).ToContainTextAsync("5", new() { Timeout = 15_000 });
    }

    [Test]
    public async Task VotingBackNavigatesToOwningProjectSessions()
    {
        var sessionName = $"E2E Back {Guid.NewGuid():N}";
        var taskTitle = $"E2E Task {Guid.NewGuid():N}";

        var projectId = await CreateProjectAndGetIdAsync("E2E Back Project");
        var sessions = new SessionsPage(Page, AspireAppFixture.BaseUrl);
        await sessions.GotoAsync(projectId);
        await sessions.CreateSessionAsync(sessionName, taskTitle);
        await sessions.ActivateAsync();

        await sessions.JoinVotingAsync();

        await Page.GetByRole(AriaRole.Button, new() { Name = "Back to sessions", Exact = true }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex($"/projects/{projectId:D}/sessions$"), new() { Timeout = 15_000 });
    }

    private async Task<Guid> CreateProjectAndGetIdAsync(string prefix)
    {
        var projects = new ProjectsPage(Page, AspireAppFixture.BaseUrl);
        return await projects.CreateProjectReturningIdAsync(prefix);
    }
}

