using Microsoft.Playwright;
using PlanDeck.E2e.Tests.Pages;

namespace PlanDeck.E2e.Tests;

[TestFixture]
[Ignore("Superseded by project-first routes; rebuilt in Phase 5")]
public class SessionsTests : PageTest
{
    public override BrowserNewContextOptions ContextOptions() => new()
    {
        IgnoreHTTPSErrors = true
    };

    [Test]
    public async Task ProjectFirstSession_CreatesProject_SelectsIt_AndCreatesVisibleSession()
    {
        var sessionName = $"E2E Session {Guid.NewGuid():N}";
        var taskTitle = $"E2E Task {Guid.NewGuid():N}";

        var sessions = new SessionsPage(Page, AspireAppFixture.BaseUrl);
        var projectName = await CreateProjectAsync("E2E Project First");

        await sessions.GotoAsync();
        await sessions.CreateSessionAsync(sessionName, taskTitle, projectName);

        await Expect(sessions.SessionEntry(sessionName)).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Expect(sessions.TaskEntry(taskTitle)).ToBeVisibleAsync(new() { Timeout = 15_000 });
    }

    [Test]
    public async Task EditTask_UpdatesTitleAndRendersMarkdownDescription()
    {
        var sessionName = $"E2E Edit {Guid.NewGuid():N}";
        var original = $"Original {Guid.NewGuid():N}";
        var renamed = $"Renamed {Guid.NewGuid():N}";

        var sessions = new SessionsPage(Page, AspireAppFixture.BaseUrl);
        var projectName = await CreateProjectAsync("E2E Edit Project");

        await sessions.GotoAsync();
        await sessions.CreateSessionAsync(sessionName, original, projectName);

        await sessions.EditTaskAsync(original, renamed, "A **bold** detail.");

        // Title updated and Markdown renders as a <strong> element (display-only).
        await Expect(sessions.ConfigTask(renamed)).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Expect(sessions.ConfigTask(renamed).Locator("strong")).ToHaveTextAsync("bold", new() { Timeout = 15_000 });
    }

    [Test]
    public async Task BulkPaste_AddsMultipleTasksWithDescription()
    {
        var sessionName = $"E2E Bulk {Guid.NewGuid():N}";
        var marker = Guid.NewGuid().ToString("N");
        var login = $"Login {marker}";
        var logout = $"Logout {marker}";
        var dashboard = $"Dashboard {marker}";

        var bulk = $"{login} | A **bold** login screen.\n{logout}\n{dashboard} | Overview widgets";

        var sessions = new SessionsPage(Page, AspireAppFixture.BaseUrl);
        var projectName = await CreateProjectAsync("E2E Bulk Project");

        await sessions.GotoAsync();
        await sessions.CreateSessionWithBulkAsync(sessionName, bulk, projectName);

        await Expect(sessions.ConfigTask(login)).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Expect(sessions.ConfigTask(logout)).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Expect(sessions.ConfigTask(dashboard)).ToBeVisibleAsync(new() { Timeout = 15_000 });

        // The piped description is parsed and rendered as Markdown.
        await Expect(sessions.ConfigTask(login).Locator("strong")).ToHaveTextAsync("bold", new() { Timeout = 15_000 });
    }

    [Test]
    public async Task ImportFromAzureDevOps_AddsWorkItemWithAdoChip()
    {
        var sessionName = $"E2E ADO {Guid.NewGuid():N}";
        var seedTask = $"Seed {Guid.NewGuid():N}";

        var sessions = new SessionsPage(Page, AspireAppFixture.BaseUrl);
        var projectName = await CreateProjectAsync("E2E ADO Project");

        await sessions.GotoAsync();
        await sessions.CreateSessionAsync(sessionName, seedTask, projectName);
        await sessions.SelectSessionAsync(sessionName);

        // The test-scheme fake serves a fixed work item with id 1001.
        await sessions.ImportAdoWorkItemAsync(1001);

        var task = sessions.ConfigTask("Import work items from Azure DevOps");
        await Expect(task).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Expect(task.GetByText("ADO #1001")).ToBeVisibleAsync(new() { Timeout = 15_000 });
    }

    [Test]
    public async Task WriteEstimateToAdo_AfterAgreedNumericEstimate_ShowsSuccess()
    {
        // The test-scheme fake serves a fixed work item (id 1001) and its WriteEstimateAsync
        // succeeds (revision + 1), so the full round-trip runs without a real Azure DevOps.
        const string adoTaskTitle = "Import work items from Azure DevOps";
        var sessionName = $"E2E WriteBack {Guid.NewGuid():N}";
        var seedTask = $"Seed {Guid.NewGuid():N}";

        var sessions = new SessionsPage(Page, AspireAppFixture.BaseUrl);
        var projectName = await CreateProjectAsync("E2E WriteBack Project");

        await sessions.GotoAsync();
        await sessions.CreateSessionAsync(sessionName, seedTask, projectName);
        await sessions.SelectSessionAsync(sessionName);
        await sessions.ImportAdoWorkItemAsync(1001);
        await sessions.ActivateAsync();

        // A numeric agreed estimate only exists after a concluded round, so drive a
        // single-voter round (owner votes, reveals, and picks) on the imported ADO task.
        await sessions.JoinVotingAsync();
        var voting = new VotingRoomPage(Page, AspireAppFixture.BaseUrl);
        await voting.WaitForLoadedAsync();
        await voting.SelectTaskAsync(adoTaskTitle);
        await voting.VoteAsync("3");
        await voting.RevealAsync();
        await voting.PickEstimateAsync("3");
        await Expect(voting.AgreedEstimate).ToContainTextAsync("3", new() { Timeout = 15_000 });

        // Back in the session config the ADO task now exposes the write-back action.
        await sessions.GotoAsync();
        await sessions.SelectSessionAsync(sessionName);
        await sessions.WriteEstimateToAdoAsync(adoTaskTitle);

        await Expect(Page.GetByText("Estimate written back to Azure DevOps."))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });
    }

    [Test]
    public async Task WriteEstimateToAdo_OnConcurrencyConflict_ShowsLocalizedError()
    {
        const string adoTaskTitle = "Estimate write-back conflict";
        var sessionName = $"E2E WriteBack Conflict {Guid.NewGuid():N}";
        var seedTask = $"Seed {Guid.NewGuid():N}";

        var sessions = new SessionsPage(Page, AspireAppFixture.BaseUrl);
        var projectName = await CreateProjectAsync("E2E Conflict Project");

        await sessions.GotoAsync();
        await sessions.CreateSessionAsync(sessionName, seedTask, projectName);
        await sessions.SelectSessionAsync(sessionName);
        await sessions.ImportAdoWorkItemAsync(1004);
        await sessions.ActivateAsync();

        await sessions.JoinVotingAsync();
        var voting = new VotingRoomPage(Page, AspireAppFixture.BaseUrl);
        await voting.WaitForLoadedAsync();
        await voting.SelectTaskAsync(adoTaskTitle);
        await voting.VoteAsync("3");
        await voting.RevealAsync();
        await voting.PickEstimateAsync("3");
        await Expect(voting.AgreedEstimate).ToContainTextAsync("3", new() { Timeout = 15_000 });

        await sessions.GotoAsync();
        await sessions.SelectSessionAsync(sessionName);
        await sessions.WriteEstimateToAdoAsync(adoTaskTitle);

        await Expect(Page.GetByText("The work item changed in Azure DevOps. Refresh and try again."))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });
    }

    [Test]
    public async Task Sessions_RendersOnMobileViewport()
    {
        var sessions = new SessionsPage(Page, AspireAppFixture.BaseUrl);

        await Page.SetViewportSizeAsync(390, 844);
        await sessions.GotoAsync();

        // Core entry point stays reachable in the single-column mobile layout.
        await Expect(sessions.CreateSessionButton).ToBeVisibleAsync(new() { Timeout = 15_000 });
    }

    private Task<string> CreateProjectAsync(string prefix) =>
        new ProjectsPage(Page, AspireAppFixture.BaseUrl).CreateUniqueProjectAsync(prefix);
}
