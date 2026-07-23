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
        _page.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("^(Create session|Utwórz sesję)$") });

    public ILocator SaveConfigurationButton =>
        _page.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("^(Save configuration|Zapisz konfigurację)$") });

    public ILocator AssignMemberButton =>
        _page.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("^(Assign|Przypisz)$") });

    public ILocator MobileMenuButton =>
        _page.GetByRole(AriaRole.Button, new() { Name = "Menu", Exact = true });

    public ILocator MobileProjectsButton =>
        _page.GetByRole(AriaRole.Button, new() { Name = "Projects", Exact = true });

    public ILocator MobileTeamsButton =>
        _page.GetByRole(AriaRole.Button, new() { Name = "Teams", Exact = true });

    private ILocator NameField =>
        _page.GetByLabel(new Regex("^(Name|Nazwa)$"));

    private ILocator TaskTitleField =>
        _page.GetByLabel(new Regex("^(Task title|Tytuł zadania)$"));

    private ILocator DescriptionField =>
        _page.GetByLabel(new Regex("^(Description|Opis)$"));

    private ILocator AddTaskButton =>
        _page.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("^(Add task|Dodaj zadanie)$") });

    private ILocator SaveButton =>
        _page.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("^(Save|Zapisz)$") });

    private ILocator OperationError => _page.GetByTestId("session-operation-error");

    private ILocator BulkToggle =>
        _page.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("^(Paste multiple tasks|Wklej wiele zadań)$") });

    private ILocator BulkTextField =>
        _page.GetByLabel(new Regex("^(Paste multiple tasks|Wklej wiele zadań)$"));

    private ILocator BulkAddButton =>
        _page.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("^(Add pasted tasks|Dodaj wklejone zadania)$") });

    private ILocator ActivateButton =>
        _page.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("^(Activate|Aktywuj)$") });

    private ILocator JoinVotingButton =>
        _page.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("^(Join voting|Dołącz do głosowania)$") });

    private ILocator AdoImportButton =>
        _page.GetByRole(AriaRole.Button, new() { Name = "Import from Azure DevOps", Exact = true });

    private ILocator AdoDialog =>
        _page.GetByRole(AriaRole.Dialog).Filter(new() { HasText = "Import from Azure DevOps" });

    public async Task GotoAsync(Guid projectId)
    {
        await _page.GotoAsync($"{_baseUrl.TrimEnd('/')}/projects/{projectId:D}/sessions", new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 120_000 });

        await _page.GetByRole(AriaRole.Heading, new() { NameRegex = new Regex("^(Planning sessions|Sesje planowania)$") })
            .WaitForAsync(new()
            {
                State = WaitForSelectorState.Visible,
                Timeout = 60_000
            });
    }

    public async Task CreateSessionAsync(
        string name,
        string adHocTaskTitle,
        string? scaleName = null)
    {
        await CreateSessionButton.ClickAsync();
        await NameField.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        await NameField.FillAsync(name);
        await SelectScaleAsync(scaleName);

        await TaskTitleField.FillAsync(adHocTaskTitle);
        await AddTaskButton.ClickAsync();

        await SaveButton.ClickAsync();
        await WaitForSessionCreationAsync(name);
    }

    public async Task CreateSessionWithBulkAsync(
        string name,
        string bulkText,
        string? scaleName = null)
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
        await WaitForSessionCreationAsync(name);
    }

    private async Task SelectScaleAsync(string? scaleName)
    {
        if (string.IsNullOrWhiteSpace(scaleName))
        {
            return;
        }

        await _page.GetByRole(AriaRole.Combobox, new() { Name = "Voting scale", Exact = true }).ClickAsync();
        await _page.GetByRole(AriaRole.Option, new() { Name = scaleName, Exact = true }).ClickAsync();
    }

    private async Task WaitForSessionCreationAsync(string sessionName)
    {
        await SessionEntry(sessionName)
            .Or(OperationError)
            .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });

        if (await OperationError.IsVisibleAsync())
        {
            throw new InvalidOperationException(
                $"Session creation failed: {await OperationError.InnerTextAsync()}");
        }
    }

    public async Task ActivateAsync()
    {
        await ActivateButton.ClickAsync();
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

        await _page.GetByRole(AriaRole.Heading, new() { NameRegex = new Regex("^(Configuration|Konfiguracja)") })
            .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
    }

    public async Task OpenMobileMenuAsync()
    {
        await MobileMenuButton.ClickAsync();
        await MobileProjectsButton.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
    }

    public async Task AssertNoHorizontalOverflowAsync()
    {
        var hasOverflow = await _page.EvaluateAsync<bool>(
            "() => document.documentElement.scrollWidth > document.documentElement.clientWidth");
        if (hasOverflow)
        {
            throw new InvalidOperationException("Detected horizontal overflow on the page.");
        }
    }

    public async Task AddTaskToSelectedAsync(string title, string? description = null)
    {
        await TaskTitleField.FillAsync(title);
        if (!string.IsNullOrEmpty(description))
        {
            await DescriptionField.FillAsync(description);
        }

        await AddTaskButton.ClickAsync();
        await TaskEntry(title).WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
    }

    public async Task EditTaskAsync(string currentTitle, string newTitle, string newDescription)
    {
        await ConfigTask(currentTitle)
            .GetByRole(AriaRole.Button, new() { NameRegex = new Regex("^(Edit task|Edytuj zadanie)$") })
            .ClickAsync();

        var dialog = _page.GetByRole(AriaRole.Dialog).Filter(new() { HasTextRegex = new Regex("Edit task|Edytuj zadanie") });
        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });

        var titleField = dialog.GetByLabel("Task title", new() { Exact = true });
        await titleField.FillAsync(newTitle);
        await titleField.BlurAsync();

        var descriptionField = dialog.GetByLabel("Description", new() { Exact = true });
        await descriptionField.FillAsync(newDescription);
        await descriptionField.BlurAsync();

        await dialog.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("^(Save|Zapisz)$") }).ClickAsync();
        await ConfigTask(newTitle).WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
    }

    public ILocator SessionEntry(string name) =>
        _page.GetByTestId("session-entry").Filter(new() { HasText = name });

    public async Task ImportAdoWorkItemAsync(int workItemId)
    {
        await _page.GetByTestId("config-ado-import").ClickAsync();

        var dialog = AdoDialog;
        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });

        await dialog.GetByRole(AriaRole.Button, new() { Name = "Import from Azure DevOps", Exact = true }).ClickAsync();

        var row = dialog.GetByRole(AriaRole.Checkbox).Filter(new() { HasText = $"#{workItemId}" });
        await row.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        await row.ClickAsync();

        await dialog.GetByRole(AriaRole.Button, new() { Name = "Add selected", Exact = true }).ClickAsync();
        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });
    }

    public ILocator ConfigTask(string title) =>
        _page.GetByTestId("config-task").Filter(new() { HasText = title });

    public ILocator WriteEstimateButton(string title) =>
        ConfigTask(title).GetByTestId("write-estimate");

    public async Task WriteEstimateToAdoAsync(string title)
    {
        var button = WriteEstimateButton(title);
        await button.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        await button.ClickAsync();
    }

    public ILocator TaskEntry(string title) =>
        _page.GetByTestId("config-task").Filter(new() { HasText = title });
}
