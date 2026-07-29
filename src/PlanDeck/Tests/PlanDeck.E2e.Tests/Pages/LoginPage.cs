using Microsoft.Playwright;

namespace PlanDeck.E2e.Tests.Pages;

public sealed class LoginPage
{
    private readonly IPage _page;
    private readonly string _baseUrl;

    public LoginPage(IPage page, string baseUrl)
    {
        _page = page;
        _baseUrl = baseUrl;
    }

    public ILocator Heading(string text) =>
        _page.GetByRole(AriaRole.Heading, new() { Name = text, Exact = true });

    public ILocator LoginInput =>
        _page.GetByLabel("Username or email", new() { Exact = true });

    public ILocator PasswordInput =>
        _page.GetByLabel("Password", new() { Exact = true });

    public ILocator SignInButton(string text) =>
        _page.GetByRole(AriaRole.Form)
            .GetByRole(AriaRole.Button, new() { Name = text, Exact = true });

    public ILocator MicrosoftButton =>
        _page.GetByRole(AriaRole.Button, new() { Name = "Sign in with a Microsoft account", Exact = true });

    public ILocator RegisterLink =>
        _page.GetByRole(AriaRole.Link, new() { Name = "Create account", Exact = true });

    public async Task GotoAsync(string? returnUrl = null, string? headingText = null)
    {
        var url = $"{_baseUrl.TrimEnd('/')}/account/login";
        if (!string.IsNullOrWhiteSpace(returnUrl))
        {
            url += $"?returnUrl={Uri.EscapeDataString(returnUrl)}";
        }

        await _page.GotoAsync(url, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 120_000 });
        await Heading(headingText ?? "Sign in to PlanDeck")
            .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 60_000 });
    }

    public async Task LoginAsync(string login, string password)
    {
        await LoginInput.FillAsync(login);
        await PasswordInput.FillAsync(password);
        await SignInButton("Log in").ClickAsync();
    }
}