using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace PlanDeck.E2e.Tests.Pages;

public class HomePage
{
    private readonly IPage _page;
    private readonly string _baseUrl;

    public HomePage(IPage page, string baseUrl)
    {
        _page = page;
        _baseUrl = baseUrl;
    }

    public ILocator AnonymousHeading =>
        _page.GetByRole(AriaRole.Heading, new()
        {
            NameRegex = new Regex("^(From backlog to shared estimate\\. In one deal\\.|Od backlogu do wspólnej estymaty\\. W jednym rozdaniu\\.)$")
        });

    public ILocator RegisteredHeading =>
        _page.GetByRole(AriaRole.Heading, new() { NameRegex = new Regex("^(Continue planning|Kontynuuj planowanie)$") });

    public ILocator GuestHeading =>
        _page.GetByRole(AriaRole.Heading, new() { NameRegex = new Regex("^(Join a planning session|Dołącz do sesji planowania)$") });

    public ILocator StartPlanningButton =>
        _page.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("^(Start planning|Rozpocznij planowanie)$") });

    public ILocator OpenProjectsButton =>
        _page.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("^(Open projects|Otwórz projekty)$") });

    public ILocator CreateProjectButton =>
        _page.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("^(Create project|Utwórz projekt)$") });

    public ILocator ManageTeamsButton =>
        _page.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("^(Manage teams|Zarządzaj zespołami)$") });

    public ILocator SessionCodeField =>
        _page.GetByLabel(new Regex("^(Session code|Kod sesji)$"));

    private ILocator JoinSessionButton =>
        _page.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("^(Join session|Dołącz do sesji)$") });

    public async Task GotoAsync()
    {
        await _page.GotoAsync(_baseUrl, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 120_000 });

        await AnonymousHeading
            .Or(RegisteredHeading)
            .Or(GuestHeading)
            .WaitForAsync(new()
            {
                State = WaitForSelectorState.Visible,
                Timeout = 60_000
            });
    }

    public async Task JoinWithCodeAsync(string code)
    {
        await SessionCodeField.FillAsync(code);
        await JoinSessionButton.ClickAsync();
        await _page.WaitForURLAsync(
            new Regex($"/join/{Regex.Escape(Uri.EscapeDataString(code.Trim()))}$"),
            new() { Timeout = 15_000 });
    }

    public async Task OpenProjectsAsync()
    {
        await OpenProjectsButton.ClickAsync();
        await _page.WaitForURLAsync(new Regex("/projects$"), new() { Timeout = 15_000 });
    }

    public async Task AssertNoHorizontalOverflowAsync()
    {
        await _page.WaitForFunctionAsync(
            "() => document.documentElement.scrollWidth <= document.documentElement.clientWidth",
            null,
            new() { Timeout = 15_000 });

        var hasOverflow = await _page.EvaluateAsync<bool>(
            "() => document.documentElement.scrollWidth > document.documentElement.clientWidth");
        Assert.That(hasOverflow, Is.False);
    }
}

