using Microsoft.Playwright;
using PlanDeck.E2e.Tests.Pages;

namespace PlanDeck.E2e.Tests;

[TestFixture]
public class VotingRoomTests : PageTest
{
    // Mirrors TestAuthenticationHandler.UserSelectionCookie: a context carrying e2e-user=b
    // authenticates as the second deterministic identity, giving two distinct voters.
    private const string UserSelectionCookie = "e2e-user";
    private const string UserBEmail = "test.userb@plandeck.local";

    public override BrowserNewContextOptions ContextOptions() => new()
    {
        IgnoreHTTPSErrors = true
    };

    [Test]
    public async Task TwoMembers_VoteRevealPick_SyncsLiveAndPersists()
    {
        var sessionName = $"E2E Voting {Guid.NewGuid():N}";
        var taskTitle = $"E2E Task {Guid.NewGuid():N}";

        // --- Context A: default identity (Test User) creates and owns the session. ---
        var sessionsA = new SessionsPage(Page, AspireAppFixture.BaseUrl);
        var membersA = new SessionMembersPage(Page);

        await sessionsA.GotoAsync();
        await sessionsA.CreateSessionAsync(sessionName, taskTitle);
        await membersA.AssignMemberAsync(UserBEmail);
        await sessionsA.ActivateAsync();

        var sessionId = await sessionsA.JoinVotingAsync();

        var votingA = new VotingRoomPage(Page, AspireAppFixture.BaseUrl);
        await votingA.WaitForLoadedAsync();

        // --- Context B: second identity (Test User B), an assigned member, joins the room. ---
        await using var contextB = await Browser.NewContextAsync(ContextOptions());
        await contextB.AddCookiesAsync(
        [
            new Cookie
            {
                Name = UserSelectionCookie,
                Value = "b",
                Url = AspireAppFixture.BaseUrl
            }
        ]);

        var pageB = await contextB.NewPageAsync();
        var votingB = new VotingRoomPage(pageB, AspireAppFixture.BaseUrl);
        await votingB.GotoAsync(sessionId);

        // Both members are present in both rosters.
        await Expect(votingA.Participants).ToHaveCountAsync(2, new() { Timeout = 15_000 });
        await Expect(votingB.Participants).ToHaveCountAsync(2, new() { Timeout = 15_000 });

        // A selects the task; the active round propagates to B (vote cards appear).
        await votingA.SelectTaskAsync(taskTitle);
        await Expect(votingB.VoteButton("3")).ToBeEnabledAsync(new() { Timeout = 15_000 });

        // Two distinct voters cast different values.
        await votingA.VoteAsync("3");
        await votingB.VoteAsync("5");

        // Before reveal both rows show "voted", but no values leak.
        await Expect(votingA.VotedStatuses).ToHaveCountAsync(2, new() { Timeout = 15_000 });
        await Expect(votingA.RevealedVotes).ToHaveCountAsync(0);

        // Reveal surfaces both votes together in both contexts.
        await votingA.RevealAsync();
        await Expect(votingA.RevealedVotes).ToHaveCountAsync(2, new() { Timeout = 15_000 });
        await Expect(votingB.RevealedVotes).ToHaveCountAsync(2, new() { Timeout = 15_000 });
        await Expect(votingB.RevealedVotes.Filter(new() { HasText = "3" })).ToHaveCountAsync(1);
        await Expect(votingB.RevealedVotes.Filter(new() { HasText = "5" })).ToHaveCountAsync(1);

        // B picks the agreed estimate; both contexts reflect it live.
        await votingB.PickEstimateAsync("5");
        await Expect(votingA.AgreedEstimate).ToContainTextAsync("5", new() { Timeout = 15_000 });
        await Expect(votingB.AgreedEstimate).ToContainTextAsync("5", new() { Timeout = 15_000 });

        // The agreed estimate persists across a full reload of B.
        await votingB.GotoAsync(sessionId);
        await votingB.SelectTaskAsync(taskTitle);
        await Expect(votingB.AgreedEstimate).ToContainTextAsync("5", new() { Timeout = 15_000 });
    }

    [Test]
    public async Task TaskAddedInSessions_PropagatesToOpenVotingRoomLive()
    {
        var sessionName = $"E2E Live {Guid.NewGuid():N}";
        var seedTask = $"Seed {Guid.NewGuid():N}";
        var liveTask = $"Live {Guid.NewGuid():N}";

        // --- Context A (owner) creates + activates the session, then joins the room. ---
        var sessionsA = new SessionsPage(Page, AspireAppFixture.BaseUrl);
        var membersA = new SessionMembersPage(Page);

        await sessionsA.GotoAsync();
        await sessionsA.CreateSessionAsync(sessionName, seedTask);
        await membersA.AssignMemberAsync(UserBEmail);
        await sessionsA.ActivateAsync();

        var sessionId = await sessionsA.JoinVotingAsync();
        var votingA = new VotingRoomPage(Page, AspireAppFixture.BaseUrl);
        await votingA.WaitForLoadedAsync();

        // --- Context B (assigned member) opens the same room. ---
        await using var contextB = await Browser.NewContextAsync(ContextOptions());
        await contextB.AddCookiesAsync(
        [
            new Cookie
            {
                Name = UserSelectionCookie,
                Value = "b",
                Url = AspireAppFixture.BaseUrl
            }
        ]);

        var pageB = await contextB.NewPageAsync();
        var votingB = new VotingRoomPage(pageB, AspireAppFixture.BaseUrl);
        await votingB.GotoAsync(sessionId);

        await Expect(votingB.TaskListItem(seedTask)).ToBeVisibleAsync(new() { Timeout = 15_000 });

        // --- A second page in context A adds a task to the Active session via /sessions. ---
        var adderPage = await Page.Context.NewPageAsync();
        var sessionsAdder = new SessionsPage(adderPage, AspireAppFixture.BaseUrl);
        await sessionsAdder.GotoAsync();
        await sessionsAdder.SelectSessionAsync(sessionName);
        await sessionsAdder.AddTaskToSelectedAsync(liveTask);

        // The new task propagates live to both open voting rooms.
        await Expect(votingA.TaskListItem(liveTask)).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Expect(votingB.TaskListItem(liveTask)).ToBeVisibleAsync(new() { Timeout = 15_000 });
    }

    [Test]
    public async Task TwoMembers_DisconnectAndReconnect_RevealShowsConsistentState()
    {
        var sessionName = $"E2E Reconnect {Guid.NewGuid():N}";
        var taskTitle = $"Reconnect Task {Guid.NewGuid():N}";

        var sessionsA = new SessionsPage(Page, AspireAppFixture.BaseUrl);
        var membersA = new SessionMembersPage(Page);
        await sessionsA.GotoAsync();
        await sessionsA.CreateSessionAsync(sessionName, taskTitle);
        await membersA.AssignMemberAsync(UserBEmail);
        await sessionsA.ActivateAsync();

        var sessionId = await sessionsA.JoinVotingAsync();
        var votingA = new VotingRoomPage(Page, AspireAppFixture.BaseUrl);
        await votingA.WaitForLoadedAsync();

        await using var contextB = await CreateUserBContextAsync();
        var pageB = await contextB.NewPageAsync();
        var votingB = new VotingRoomPage(pageB, AspireAppFixture.BaseUrl);
        await votingB.GotoAsync(sessionId);

        await votingA.SelectTaskAsync(taskTitle);
        await Expect(votingB.VoteButton("3")).ToBeEnabledAsync(new() { Timeout = 15_000 });

        await votingA.VoteAsync("3");
        await votingB.VoteAsync("5");
        await Expect(votingA.VotedStatuses).ToHaveCountAsync(2, new() { Timeout = 15_000 });

        await pageB.CloseAsync();
        await votingA.RevealAsync();
        await Expect(votingA.RevealedVotes).ToHaveCountAsync(2, new() { Timeout = 15_000 });

        var pageBReconnect = await contextB.NewPageAsync();
        var votingBReconnect = new VotingRoomPage(pageBReconnect, AspireAppFixture.BaseUrl);
        await votingBReconnect.GotoAsync(sessionId);

        await Expect(votingBReconnect.RevealedVotes).ToHaveCountAsync(2, new() { Timeout = 15_000 });
        await Expect(votingBReconnect.RevealedVotes.Filter(new() { HasText = "3" })).ToHaveCountAsync(1);
        await Expect(votingBReconnect.RevealedVotes.Filter(new() { HasText = "5" })).ToHaveCountAsync(1);
    }

    [Test]
    public async Task EstimateSelect_PersistsAcrossPageReload()
    {
        var sessionName = $"E2E Persist {Guid.NewGuid():N}";
        var taskTitle = $"Persist Task {Guid.NewGuid():N}";

        var sessions = new SessionsPage(Page, AspireAppFixture.BaseUrl);
        await sessions.GotoAsync();
        await sessions.CreateSessionAsync(sessionName, taskTitle);
        await sessions.ActivateAsync();

        var sessionId = await sessions.JoinVotingAsync();
        var voting = new VotingRoomPage(Page, AspireAppFixture.BaseUrl);
        await voting.WaitForLoadedAsync();

        await voting.SelectTaskAsync(taskTitle);
        await voting.VoteAsync("5");
        await voting.RevealAsync();
        await Expect(voting.RevealedVotes).ToHaveCountAsync(1, new() { Timeout = 15_000 });

        await voting.PickEstimateAsync("5");
        await Expect(voting.AgreedEstimate).ToContainTextAsync("5", new() { Timeout = 15_000 });

        await voting.GotoAsync(sessionId);
        await voting.SelectTaskAsync(taskTitle);
        await Expect(voting.AgreedEstimate).ToContainTextAsync("5", new() { Timeout = 15_000 });
    }

    [Test]
    public async Task SessionConfig_ScaleAndTasks_FeedIntoVotingRound()
    {
        var sessionName = $"E2E Config {Guid.NewGuid():N}";
        var taskA = $"Config Task A {Guid.NewGuid():N}";
        var taskB = $"Config Task B {Guid.NewGuid():N}";

        var sessions = new SessionsPage(Page, AspireAppFixture.BaseUrl);
        var members = new SessionMembersPage(Page);
        await sessions.GotoAsync();
        await sessions.CreateSessionWithBulkAsync(
            sessionName,
            $"{taskA}{Environment.NewLine}{taskB}",
            "T-shirt sizes");
        await members.AssignMemberAsync(UserBEmail);
        await sessions.ActivateAsync();

        var sessionId = await sessions.JoinVotingAsync();
        var voting = new VotingRoomPage(Page, AspireAppFixture.BaseUrl);
        await voting.GotoAsync(sessionId);

        await Expect(voting.TaskListItem(taskA)).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Expect(voting.TaskListItem(taskB)).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Expect(voting.VoteButton("XS")).ToBeVisibleAsync();
        await Expect(voting.VoteButton("S")).ToBeVisibleAsync();
        await Expect(voting.VoteButton("M")).ToBeVisibleAsync();
        await Expect(voting.VoteButton("L")).ToBeVisibleAsync();
        await Expect(voting.VoteButton("XL")).ToBeVisibleAsync();
        await Expect(voting.VoteButton("?")).ToBeVisibleAsync();
    }

    private async Task<IBrowserContext> CreateUserBContextAsync()
    {
        var context = await Browser.NewContextAsync(ContextOptions());
        await context.AddCookiesAsync(
        [
            new Cookie
            {
                Name = UserSelectionCookie,
                Value = "b",
                Url = AspireAppFixture.BaseUrl
            }
        ]);

        return context;
    }
}
