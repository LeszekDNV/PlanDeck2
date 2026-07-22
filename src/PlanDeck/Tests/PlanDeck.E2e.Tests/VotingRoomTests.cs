using Microsoft.Playwright;
using PlanDeck.E2e.Tests.Pages;

namespace PlanDeck.E2e.Tests;

[TestFixture]
[Ignore("Superseded by project-first routes; rebuilt in Phase 5")]
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

        // --- Context A: default identity (Test User) creates and owns the session. ---
        var sessionsA = new SessionsPage(Page, AspireAppFixture.BaseUrl);
        var membersA = new SessionMembersPage(Page);
        var projectName = await CreateProjectAsync("E2E Voting Project");

        await sessionsA.GotoAsync();
        await sessionsA.CreateSessionAsync(sessionName, taskTitle, projectName);
        await membersA.AssignMemberAsync(MemberEmail);
        await sessionsA.ActivateAsync();

        var sessionId = await sessionsA.JoinVotingAsync();

        var votingA = new VotingRoomPage(Page, AspireAppFixture.BaseUrl);
        await votingA.WaitForLoadedAsync();

        // --- Context B: deterministic member identity joins the same voting room. ---
        await using var contextB = await E2eIdentityContextFactory.CreateMemberContextAsync(
            Browser,
            AspireAppFixture.BaseUrl,
            ContextOptions());

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
        await Expect(votingA.RevealedVotes.Filter(new() { HasText = "3" })).ToHaveCountAsync(1);
        await Expect(votingA.RevealedVotes.Filter(new() { HasText = "5" })).ToHaveCountAsync(1);
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
        var projectName = await CreateProjectAsync("E2E Live Project");

        await sessionsA.GotoAsync();
        await sessionsA.CreateSessionAsync(sessionName, seedTask, projectName);
        await membersA.AssignMemberAsync(MemberEmail);
        await sessionsA.ActivateAsync();

        var sessionId = await sessionsA.JoinVotingAsync();
        var votingA = new VotingRoomPage(Page, AspireAppFixture.BaseUrl);
        await votingA.WaitForLoadedAsync();

        // --- Context B (assigned member) opens the same room. ---
        await using var contextB = await E2eIdentityContextFactory.CreateMemberContextAsync(
            Browser,
            AspireAppFixture.BaseUrl,
            ContextOptions());

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
        var projectName = await CreateProjectAsync("E2E Reconnect Project");
        await sessionsA.GotoAsync();
        await sessionsA.CreateSessionAsync(sessionName, taskTitle, projectName);
        await membersA.AssignMemberAsync(MemberEmail);
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
        await Expect(votingA.Participant("Test Member"))
            .ToHaveAttributeAsync("data-online", "false", new() { Timeout = 15_000 });
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
        var members = new SessionMembersPage(Page);
        var projectName = await CreateProjectAsync("E2E Persist Project");
        await sessions.GotoAsync();
        await sessions.CreateSessionAsync(sessionName, taskTitle, projectName);
        await members.AssignMemberAsync(MemberEmail);
        await sessions.ActivateAsync();

        var sessionId = await sessions.JoinVotingAsync();
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

        await votingA.RevealAsync();
        await Expect(votingA.RevealedVotes).ToHaveCountAsync(2, new() { Timeout = 15_000 });
        await Expect(votingB.RevealedVotes).ToHaveCountAsync(2, new() { Timeout = 15_000 });

        await votingA.PickEstimateAsync("5");
        await Expect(votingB.AgreedEstimate).ToContainTextAsync("5", new() { Timeout = 15_000 });

        await votingA.GotoAsync(sessionId);
        await votingA.SelectTaskAsync(taskTitle);
        await Expect(votingA.AgreedEstimate).ToContainTextAsync("5", new() { Timeout = 15_000 });
    }

    [Test]
    public async Task SessionConfig_ScaleAndTasks_FeedIntoVotingRound()
    {
        var sessionName = $"E2E Config {Guid.NewGuid():N}";
        var taskA = $"Config Task A {Guid.NewGuid():N}";
        var taskB = $"Config Task B {Guid.NewGuid():N}";

        var sessions = new SessionsPage(Page, AspireAppFixture.BaseUrl);
        var members = new SessionMembersPage(Page);
        var projectName = await CreateProjectAsync("E2E Config Project");
        await sessions.GotoAsync();
        await sessions.CreateSessionWithBulkAsync(
            sessionName,
            $"{taskA}{Environment.NewLine}{taskB}",
            projectName,
            "T-shirt sizes");
        await members.AssignMemberAsync(MemberEmail);
        await sessions.ActivateAsync();

        var sessionId = await sessions.JoinVotingAsync();
        var voting = new VotingRoomPage(Page, AspireAppFixture.BaseUrl);
        await voting.GotoAsync(sessionId);

        await Expect(voting.TaskListItems).ToHaveTextAsync([taskA, taskB], new() { Timeout = 15_000 });
        await Expect(voting.VoteButton("XS")).ToBeVisibleAsync();
        await Expect(voting.VoteButton("S")).ToBeVisibleAsync();
        await Expect(voting.VoteButton("M")).ToBeVisibleAsync();
        await Expect(voting.VoteButton("L")).ToBeVisibleAsync();
        await Expect(voting.VoteButton("XL")).ToBeVisibleAsync();
        await Expect(voting.VoteButton("?")).ToBeVisibleAsync();
    }

    private async Task<IBrowserContext> CreateUserBContextAsync()
        => await E2eIdentityContextFactory.CreateMemberContextAsync(
            Browser,
            AspireAppFixture.BaseUrl,
            ContextOptions());

    private Task<string> CreateProjectAsync(string prefix) =>
        new ProjectsPage(Page, AspireAppFixture.BaseUrl).CreateUniqueProjectAsync(prefix);
}
