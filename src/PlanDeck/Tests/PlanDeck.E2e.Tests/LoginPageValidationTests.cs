using Microsoft.Playwright;
using PlanDeck.E2e.Tests.Pages;

namespace PlanDeck.E2e.Tests;

[TestFixture]
public class LoginPageValidationTests : PageTest
{
    public override BrowserNewContextOptions ContextOptions() => new()
    {
        IgnoreHTTPSErrors = true
    };

    [Test]
    public async Task EmptyForm_ShouldShowEnglishValidationMessages()
    {
        await SetAnonymousAsync();
        await SetCultureAsync("en");

        var loginPage = new LoginPage(Page, AspireAppFixture.BaseUrl);
        await loginPage.GotoAsync(headingText: "Sign in to PlanDeck");

        await loginPage.SignInButton("Log in").ClickAsync();

        await Expect(Page.GetByText("Please enter your username or email.", new() { Exact = true }))
            .ToBeVisibleAsync();
        await Expect(Page.GetByText("Password is required.", new() { Exact = true }))
            .ToBeVisibleAsync();
    }

    [Test]
    public async Task EmptyForm_ShouldShowPolishValidationMessages()
    {
        await SetAnonymousAsync();
        await SetCultureAsync("pl");

        var loginPage = new LoginPage(Page, AspireAppFixture.BaseUrl);
        await loginPage.GotoAsync(headingText: "Zaloguj się do PlanDeck");

        await loginPage.SignInButton("Zaloguj").ClickAsync();

        await Expect(Page.GetByText("Podaj nazwę użytkownika lub adres email.", new() { Exact = true }))
            .ToBeVisibleAsync();
        await Expect(Page.GetByText("Hasło jest wymagane.", new() { Exact = true }))
            .ToBeVisibleAsync();
    }

    private async Task SetAnonymousAsync()
    {
        var host = new Uri(AspireAppFixture.BaseUrl).Host;
        await Context.AddCookiesAsync([
            new()
            {
                Name = "e2e-user",
                Value = "anonymous",
                Domain = host,
                Path = "/"
            }
        ]);
    }

    private async Task SetCultureAsync(string culture)
    {
        await Page.GotoAsync(
            AspireAppFixture.BaseUrl,
            new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 120_000 });
        await Page.EvaluateAsync($"() => localStorage.setItem('BlazorCulture', '{culture}')");
    }
}