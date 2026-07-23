using Microsoft.Playwright;

namespace PlanDeck.E2e.Tests.Pages;

public class TeamsPage
{
    private readonly IPage _page;
    private readonly string _baseUrl;

    public TeamsPage(IPage page, string baseUrl)
    {
        _page = page;
        _baseUrl = baseUrl;
    }

    private ILocator CreateTeamButton =>
        _page.GetByRole(AriaRole.Button, new() { Name = "Create team" });

    private ILocator NameField =>
        _page.GetByLabel("Name", new() { Exact = true });

    private ILocator SaveButton =>
        _page.GetByRole(AriaRole.Button, new() { Name = "Save" });

    private ILocator EmailField =>
        _page.GetByLabel("Email", new() { Exact = true });

    private ILocator AddMemberButton =>
        _page.GetByRole(AriaRole.Button, new() { Name = "Add member" });

    public async Task GotoAsync()
    {
        await _page.GotoAsync($"{_baseUrl.TrimEnd('/')}/teams", new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 120_000 });

        // Wait for the WASM app to boot and the authenticated Teams view to render.
        await CreateTeamButton.WaitForAsync(new()
        {
            State = WaitForSelectorState.Visible,
            Timeout = 60_000
        });
    }

    public async Task CreateTeamAsync(string name)
    {
        await CreateTeamButton.ClickAsync();
        await NameField.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        await NameField.FillAsync(name);
        await SaveButton.ClickAsync();

        // The created team appears in the list as a selectable button.
        await _page.GetByRole(AriaRole.Button, new() { Name = name })
            .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
    }

    public async Task AddMemberAsync(string email)
    {
        await EmailField.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        await EmailField.FillAsync(email);
        await AddMemberButton.ClickAsync();
    }

    public async Task RemoveMemberAsync(string email)
    {
        await MemberRow(email)
            .GetByRole(AriaRole.Button, new() { Name = "Remove" })
            .ClickAsync();

        // Confirm the removal in the MudMessageBox dialog.
        await _page.Locator(".mud-dialog")
            .GetByRole(AriaRole.Button, new() { Name = "Remove" })
            .ClickAsync();
    }

    public ILocator MemberEntry(string email) => _page.GetByText(email);

    private ILocator MemberRow(string email) =>
        _page.Locator(".mud-list-item").Filter(new() { HasText = email });
}


