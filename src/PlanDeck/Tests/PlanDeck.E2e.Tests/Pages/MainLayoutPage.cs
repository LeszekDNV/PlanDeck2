using Microsoft.Playwright;

namespace PlanDeck.E2e.Tests.Pages;

public sealed class MainLayoutPage
{
    private readonly IPage _page;
    private readonly string _baseUrl;

    public MainLayoutPage(IPage page, string baseUrl)
    {
        _page = page;
        _baseUrl = baseUrl;
    }

    public ILocator LogOutButton =>
        _page.GetByRole(AriaRole.Toolbar)
            .GetByRole(AriaRole.Button, new() { Name = "Log out", Exact = true });

    public ILocator LogInButton =>
        _page.GetByRole(AriaRole.Toolbar)
            .GetByRole(AriaRole.Button, new() { Name = "Log in", Exact = true });

    public async Task OpenAuthenticatedApplicationAsync()
    {
        await _page.GotoAsync(
            _baseUrl,
            new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 120_000 });
        await LogOutButton.WaitForAsync(new()
        {
            State = WaitForSelectorState.Visible,
            Timeout = 60_000
        });
    }

    public async Task LogOutAsync()
    {
        await LogOutButton.ClickAsync();
        await LogInButton.WaitForAsync(new()
        {
            State = WaitForSelectorState.Visible,
            Timeout = 60_000
        });
    }

    public async Task ReloadAnonymousApplicationAsync()
    {
        await _page.ReloadAsync(new()
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = 120_000
        });
        await LogInButton.WaitForAsync(new()
        {
            State = WaitForSelectorState.Visible,
            Timeout = 60_000
        });
    }
}
