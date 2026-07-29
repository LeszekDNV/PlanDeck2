using Microsoft.Playwright;
using PlanDeck.E2e.Tests.Pages;

namespace PlanDeck.E2e.Tests;

[TestFixture]
public class HomePageTests : PageTest
{
    public override BrowserNewContextOptions ContextOptions() => new()
    {
        IgnoreHTTPSErrors = true
    };

    [Test]
    public async Task AnonymousHome_ShowsProductStory_AndRoutesSessionCode()
    {
        var home = new HomePage(Page, AspireAppFixture.BaseUrl);
        await home.GotoAsync();

        await Expect(home.AnonymousHeading).ToBeVisibleAsync();
        await Expect(home.StartPlanningButton).ToBeVisibleAsync();

        await home.JoinWithCodeAsync("HOME123");
        await Expect(Page).ToHaveURLAsync(new Regex("/join/HOME123$"));
    }

    [Test]
    public async Task RegisteredHome_StaysOnRoot_AndOffersProjectActions()
    {
        await LocalAccountFlow.RegisterConfirmAndLoginAsync(Page, AspireAppFixture.BaseUrl);

        var home = new HomePage(Page, AspireAppFixture.BaseUrl);
        await home.GotoAsync();

        await Expect(Page).ToHaveURLAsync(new Regex("/$"));
        await Expect(home.RegisteredHeading).ToBeVisibleAsync();
        await Expect(home.CreateProjectButton).ToBeVisibleAsync();
        await Expect(home.ManageTeamsButton).ToBeVisibleAsync();

        await home.OpenProjectsAsync();
    }

    [Test]
    public async Task GuestHome_AfterJoiningActiveSession_ShowsParticipantOnlyActions()
    {
        await using var ownerContext = await Browser.NewContextAsync(new()
        {
            IgnoreHTTPSErrors = true
        });
        var ownerPage = await ownerContext.NewPageAsync();
        Guid? projectId = null;

        try
        {
            await LocalAccountFlow.RegisterConfirmAndLoginAsync(ownerPage, AspireAppFixture.BaseUrl);

            var projects = new ProjectsPage(ownerPage, AspireAppFixture.BaseUrl);
            projectId = await projects.CreateProjectReturningIdAsync("Home guest");

            var sessions = new SessionsPage(ownerPage, AspireAppFixture.BaseUrl);
            await sessions.GotoAsync(projectId.Value);
            var suffix = Guid.NewGuid().ToString("N");
            await sessions.CreateSessionAsync($"Home guest session {suffix}", $"Home guest task {suffix}");
            await sessions.ActivateAsync();
            var shareCode = await sessions.GetActiveShareCodeAsync();

            var join = new JoinSessionPage(Page, AspireAppFixture.BaseUrl);
            await join.GotoAsync(shareCode);
            await join.JoinAsync($"Guest {suffix[..8]}");

            var home = new HomePage(Page, AspireAppFixture.BaseUrl);
            await home.GotoAsync();

            await Expect(Page).ToHaveURLAsync(new Regex("/$"));
            await Expect(home.GuestHeading).ToBeVisibleAsync();
            await Expect(home.SessionCodeField).ToBeVisibleAsync();
            var projectActionCount =
                await home.OpenProjectsButton.CountAsync() +
                await home.CreateProjectButton.CountAsync() +
                await home.ManageTeamsButton.CountAsync();
            Assert.That(projectActionCount, Is.Zero);
        }
        finally
        {
            if (projectId.HasValue)
            {
                var details = new ProjectDetailsPage(ownerPage, AspireAppFixture.BaseUrl);
                await details.GotoAsync(projectId.Value);
                await details.DeleteProjectAsync();
            }
        }
    }

    [Test]
    public async Task AnonymousHome_At375Pixels_RemainsUsableWithoutHorizontalOverflow()
    {
        await Page.SetViewportSizeAsync(375, 812);

        var home = new HomePage(Page, AspireAppFixture.BaseUrl);
        await home.GotoAsync();

        await Expect(home.AnonymousHeading).ToBeVisibleAsync();
        await Expect(home.StartPlanningButton).ToBeVisibleAsync();
        await Expect(home.SessionCodeField).ToBeVisibleAsync();
        await home.AssertNoHorizontalOverflowAsync();
    }
}
