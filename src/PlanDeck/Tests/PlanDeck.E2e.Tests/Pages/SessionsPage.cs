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

    private ILocator CreateSessionButton =>
        _page.GetByRole(AriaRole.Button, new() { Name = "Create session" });

    private ILocator NameField =>
        _page.GetByLabel("Name", new() { Exact = true });

    private ILocator TaskTitleField =>
        _page.GetByLabel("Task title", new() { Exact = true });

    private ILocator AddTaskButton =>
        _page.GetByRole(AriaRole.Button, new() { Name = "Add task" });

    private ILocator SaveButton =>
        _page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true });

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

    public async Task CreateSessionAsync(string name, string adHocTaskTitle)
    {
        await CreateSessionButton.ClickAsync();
        await NameField.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        await NameField.FillAsync(name);

        await TaskTitleField.FillAsync(adHocTaskTitle);
        await AddTaskButton.ClickAsync();

        await SaveButton.ClickAsync();

        // The created session appears in the list as a selectable button.
        await SessionEntry(name).WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
    }

    private ILocator ActivateButton =>
        _page.GetByRole(AriaRole.Button, new() { Name = "Activate", Exact = true });

    private ILocator JoinVotingButton =>
        _page.GetByRole(AriaRole.Button, new() { Name = "Join voting" });

    public async Task ActivateAsync()
    {
        await ActivateButton.ClickAsync();

        // Activation locks the session and reveals the "Join voting" entry.
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

    public ILocator SessionEntry(string name) =>
        _page.GetByRole(AriaRole.Button, new() { Name = name });

    public ILocator TaskEntry(string title) => _page.GetByText(title);
}
