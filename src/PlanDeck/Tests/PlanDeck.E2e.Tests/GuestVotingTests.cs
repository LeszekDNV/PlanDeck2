using System.Text.RegularExpressions;
using Microsoft.Playwright;
using PlanDeck.E2e.Tests.Pages;

namespace PlanDeck.E2e.Tests;

[TestFixture]
public class GuestVotingTests : PageTest
{
    // Mirrors TestAuthenticationHandler.GuestSessionCookie: a context carrying this cookie is
    // authenticated as a deterministic, session-scoped guest (no membership). A cookie (not a
    // header) is used so the identity also rides the SignalR WebSocket handshake — the only way to
    // represent an account-less guest in the hub under the deterministic E2E test-auth scheme.
    private const string GuestSessionCookie = "e2e-guest-sid";

    public override BrowserNewContextOptions ContextOptions() => new()
    {
        IgnoreHTTPSErrors = true
    };

    [Test]
    public async Task Guest_JoinsActiveSession_Votes_AndSeesReveal_WithoutControls()
    {
        var sessionName = $"E2E Guest {Guid.NewGuid():N}";
        var taskTitle = $"E2E Task {Guid.NewGuid():N}";

        // --- Context A: organizer (Test User) creates and activates the session. ---
        var sessionsA = new SessionsPage(Page, AspireAppFixture.BaseUrl);
        var projectName = await new ProjectsPage(Page, AspireAppFixture.BaseUrl)
            .CreateUniqueProjectAsync("E2E Guest Project");
        await sessionsA.GotoAsync();
        await sessionsA.CreateSessionAsync(sessionName, taskTitle, projectName);
        await sessionsA.ActivateAsync();

        var sessionId = await sessionsA.JoinVotingAsync();
        var votingA = new VotingRoomPage(Page, AspireAppFixture.BaseUrl);
        await votingA.WaitForLoadedAsync();

        // --- Guest context: account-less, scoped to this session via the guest cookie. ---
        await using var guestContext = await Browser.NewContextAsync(ContextOptions());
        await guestContext.AddCookiesAsync(
        [
            new Cookie
            {
                Name = GuestSessionCookie,
                Value = sessionId.ToString(),
                Url = AspireAppFixture.BaseUrl
            }
        ]);

        var guestPage = await guestContext.NewPageAsync();
        var votingGuest = new VotingRoomPage(guestPage, AspireAppFixture.BaseUrl);
        await votingGuest.GotoAsync(sessionId);

        // Organizer + guest are both present in both rosters.
        await Expect(votingA.Participants).ToHaveCountAsync(2, new() { Timeout = 15_000 });
        await Expect(votingGuest.Participants).ToHaveCountAsync(2, new() { Timeout = 15_000 });

        // The guest sees a vote-only room: no moderator controls are rendered.
        await Expect(votingGuest.RevealButton).ToHaveCountAsync(0);
        await Expect(votingGuest.ResetButton).ToHaveCountAsync(0);

        // Organizer opens the round; the guest can cast a vote.
        await votingA.SelectTaskAsync(taskTitle);
        await Expect(votingGuest.VoteButton("5")).ToBeEnabledAsync(new() { Timeout = 15_000 });
        await votingGuest.VoteAsync("5");

        // The guest's vote stays hidden until the organizer reveals.
        await Expect(votingGuest.RevealedVotes).ToHaveCountAsync(0);
        await votingA.RevealAsync();

        // After reveal, the guest's value surfaces live in both the organizer and guest views.
        await Expect(votingA.RevealedVotes.Filter(new() { HasText = "5" }))
            .ToHaveCountAsync(1, new() { Timeout = 15_000 });
        await Expect(votingGuest.RevealedVotes.Filter(new() { HasText = "5" }))
            .ToHaveCountAsync(1, new() { Timeout = 15_000 });
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
}
