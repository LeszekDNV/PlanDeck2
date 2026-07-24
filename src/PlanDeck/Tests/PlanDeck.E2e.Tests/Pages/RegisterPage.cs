using Microsoft.Playwright;

namespace PlanDeck.E2e.Tests.Pages;

public sealed class RegisterPage
{
    private readonly IPage _page;
    private readonly string _baseUrl;

    public RegisterPage(IPage page, string baseUrl)
    {
        _page = page;
        _baseUrl = baseUrl;
    }

    public ILocator Heading =>
        _page.GetByRole(AriaRole.Heading, new() { Name = "Create your PlanDeck account", Exact = true });

    public ILocator EmailInput =>
        _page.GetByLabel("Email", new() { Exact = true });

    public ILocator UserNameInput =>
        _page.GetByLabel("Username", new() { Exact = true });

    public ILocator FirstNameInput =>
        _page.GetByLabel("First name", new() { Exact = true });

    public ILocator LastNameInput =>
        _page.GetByLabel("Last name", new() { Exact = true });

    public ILocator PasswordInput =>
        _page.GetByLabel("Password", new() { Exact = true });

    public ILocator ConfirmPasswordInput =>
        _page.GetByLabel("Confirm password", new() { Exact = true });

    public ILocator CreateAccountButton =>
        _page.GetByRole(AriaRole.Button, new() { Name = "Create account", Exact = true });

    public ILocator MicrosoftButton =>
        _page.GetByRole(AriaRole.Button, new() { Name = "Create account with Microsoft", Exact = true });

    public ILocator SignInLink =>
        _page.GetByRole(AriaRole.Link, new() { Name = "Already have an account? Sign in", Exact = true });

    public async Task GotoAsync(string? invitationToken = null)
    {
        var url = $"{_baseUrl.TrimEnd('/')}/account/register";
        if (!string.IsNullOrWhiteSpace(invitationToken))
        {
            url += $"?invitationToken={Uri.EscapeDataString(invitationToken)}";
        }

        await _page.GotoAsync(url, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 120_000 });
        await Heading.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 60_000 });
    }

    public async Task RegisterAsync(
        string email,
        string userName,
        string firstName,
        string lastName,
        string password)
    {
        await EmailInput.FillAsync(email);
        await UserNameInput.FillAsync(userName);
        await FirstNameInput.FillAsync(firstName);
        await LastNameInput.FillAsync(lastName);
        await PasswordInput.FillAsync(password);
        await ConfirmPasswordInput.FillAsync(password);
        await CreateAccountButton.ClickAsync();
    }
}
