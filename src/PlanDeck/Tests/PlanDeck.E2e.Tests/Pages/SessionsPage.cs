using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace PlanDeck.E2e.Tests.Pages;

public class SessionsPage
{
    private readonly IPage _page;
    private readonly string _baseUrl;

    public SessionsPage(IPage page, string baseUrl)
    {
        _page = page;
        _baseUrl = baseUrl;
    }

    public ILocator CreateSessionButton =>
        _page.GetByRole(AriaRole.Button, new() { Name = "Create session" });

    private ILocator NameField =>
        _page.GetByLabel("Name", new() { Exact = true });

    private ILocator TaskTitleField =>
        _page.GetByLabel("Task title", new() { Exact = true });

    private ILocator DescriptionField =>
        _page.GetByLabel("Description", new() { Exact = true });

    private ILocator AddTaskButton =>
        _page.GetByRole(AriaRole.Button, new() { Name = "Add task" });

    private ILocator SaveButton =>
        _page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true });

    private ILocator BulkToggle =>
        _page.GetByRole(AriaRole.Button, new() { Name = "Paste multiple tasks" });

    private ILocator BulkTextField =>
        _page.GetByLabel("Paste multiple tasks");

    private ILocator BulkAddButton =>
        _page.GetByRole(AriaRole.Button, new() { Name = "Add pasted tasks" });

    public async Task GotoAsync()
    {
        await _page.GotoAsync($"{_baseUrl.TrimEnd('/')}/sessions", new() { WaitUntil = WaitUntilState.NetworkIdle });

        // Wait for the WASM app to boot and the authenticated Sessions view to render.
        await CreateSessionButton.WaitForAsync(new()
        {
            State = WaitForSelectorState.Visible,
            Timeout = 60_000
        });
    }

    public async Task CreateSessionAsync(string name, string adHocTaskTitle, string? scaleName = null)
    {
        await CreateSessionButton.ClickAsync();
        await NameField.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        await NameField.FillAsync(name);
        await SelectScaleAsync(scaleName);

        await TaskTitleField.FillAsync(adHocTaskTitle);
        await AddTaskButton.ClickAsync();

        await SaveButton.ClickAsync();

        // The created session appears in the list and is auto-selected.
        await SessionEntry(name).WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
    }

    public async Task CreateSessionWithBulkAsync(string name, string bulkText, string? scaleName = null)
    {
        await CreateSessionButton.ClickAsync();
        await NameField.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        await NameField.FillAsync(name);
        await SelectScaleAsync(scaleName);

        await BulkToggle.ClickAsync();
        await BulkTextField.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        await BulkTextField.FillAsync(bulkText);
        await BulkAddButton.ClickAsync();

        await SaveButton.ClickAsync();
        await SessionEntry(name).WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
    }

    private async Task SelectScaleAsync(string? scaleName)
    {
        if (string.IsNullOrWhiteSpace(scaleName))
        {
            return;
        }

        // MudSelect renders a hidden input for ARIA; click the visible input container instead.
        await _page.Locator(".mud-input-control")
            .Filter(new() { HasText = "Voting scale" })
            .First
            .ClickAsync();
        await _page.GetByRole(AriaRole.Option, new() { Name = scaleName, Exact = true }).ClickAsync();
    }

    private ILocator ActivateButton =>
        _page.GetByRole(AriaRole.Button, new() { Name = "Activate", Exact = true });

    private ILocator JoinVotingButton =>
        _page.GetByRole(AriaRole.Button, new() { Name = "Join voting" });

    public async Task ActivateAsync()
    {
        await ActivateButton.ClickAsync();

        // Activation locks the config and reveals the "Join voting" entry.
        await JoinVotingButton.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
    }

    public async Task<Guid> JoinVotingAsync()
    {
        await JoinVotingButton.ClickAsync();
        await _page.WaitForURLAsync(
            new Regex("/voting/[0-9a-fA-F-]{36}$"),
            new() { Timeout = 15_000 });

        var match = Regex.Match(_page.Url, "/voting/([0-9a-fA-F-]{36})");
        return Guid.Parse(match.Groups[1].Value);
    }

    public async Task SelectSessionAsync(string name)
    {
        await SessionEntry(name).ClickAsync();

        // The config + tasks panel renders for the selected session.
        await AddTaskButton.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
    }

    public async Task AddTaskToSelectedAsync(string title, string? description = null)
    {
        await TaskTitleField.FillAsync(title);
        if (!string.IsNullOrEmpty(description))
        {
            await DescriptionField.FillAsync(description);
        }

        await AddTaskButton.ClickAsync();
        await TaskEntry(title).First.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
    }

    public async Task EditTaskAsync(string currentTitle, string newTitle, string newDescription)
    {
        await ConfigTask(currentTitle)
            .GetByRole(AriaRole.Button, new() { Name = "Edit task" })
            .ClickAsync();

        var dialog = _page.Locator(".mud-dialog").Filter(new() { HasText = "Edit task" });
        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });

        var titleField = dialog.GetByLabel("Task title", new() { Exact = true });
        await titleField.FillAsync(newTitle);
        await titleField.BlurAsync();

        var descriptionField = dialog.GetByLabel("Description", new() { Exact = true });
        await descriptionField.FillAsync(newDescription);
        await descriptionField.BlurAsync();

        await dialog.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();

        // Success is the renamed task surfacing in the (re-rendered) task list.
        await ConfigTask(newTitle).WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
    }

    public ILocator SessionEntry(string name) =>
        _page.Locator("[data-testid=session-entry]").Filter(new() { HasText = name });

    private ILocator AdoImportButton =>
        _page.GetByRole(AriaRole.Button, new() { Name = "Import from Azure DevOps" });

    private ILocator AdoDialog =>
        _page.Locator(".mud-dialog").Filter(new() { HasText = "Import from Azure DevOps" });

    public async Task ImportAdoWorkItemAsync(int workItemId)
    {
        // Open the import dialog from the config panel.
        await AdoImportButton.First.ClickAsync();

        var dialog = AdoDialog;
        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });

        // The load button shares the "Import from Azure DevOps" label, so scope to the dialog.
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Import from Azure DevOps" }).ClickAsync();

        // Select the requested work item by clicking its checkbox label.
        var row = dialog.Locator("label.mud-checkbox").Filter(new() { HasText = $"#{workItemId}" });
        await row.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        await row.ClickAsync();

        // "Add selected" closes the dialog and persists the tasks.
        await dialog.Locator("button:has-text('Add selected')").ClickAsync();
        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });
    }

    public ILocator ConfigTask(string title) =>
        _page.Locator("[data-testid=config-task]").Filter(new() { HasText = title });

    public ILocator WriteEstimateButton(string title) =>
        ConfigTask(title).Locator("[data-testid=write-estimate]");

    public async Task WriteEstimateToAdoAsync(string title)
    {
        var button = WriteEstimateButton(title);
        await button.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        await button.ClickAsync();
    }

    public ILocator TaskEntry(string title) =>
        _page.Locator("[data-testid=config-task]").Filter(new() { HasText = title });
}
