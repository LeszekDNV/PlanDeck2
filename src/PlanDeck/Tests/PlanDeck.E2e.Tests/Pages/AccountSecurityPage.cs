using Microsoft.Playwright;

namespace PlanDeck.E2e.Tests.Pages;

public sealed class AccountSecurityPage
{
    private readonly IPage _page;
    private readonly string _baseUrl;

    public AccountSecurityPage(IPage page, string baseUrl)
    {
        _page = page;
        _baseUrl = baseUrl;
    }

    public ILocator Heading =>
        _page.GetByRole(AriaRole.Heading, new() { Name = "Account security", Exact = true });

    public ILocator UserNameText =>
        _page.GetByText("Username:", new() { Exact = false });

    public ILocator EmailText =>
        _page.GetByText("Email:", new() { Exact = false });

    public ILocator LinkMicrosoftButton =>
        _page.GetByRole(AriaRole.Button, new() { Name = "Link Microsoft account", Exact = true });

    public ILocator PasswordInput =>
        _page.GetByLabel("Password", new() { Exact = true });

    public async Task GotoAsync()
    {
        var url = $"{_baseUrl.TrimEnd('/')}/account/security";
        await _page.GotoAsync(url, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 120_000 });
        await Heading.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 60_000 });
    }

    public async Task StartLinkMicrosoftAsync(string password)
    {
        await LinkMicrosoftButton.ClickAsync();
        await PasswordInput.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });
        await PasswordInput.FillAsync(password);
    }
}
