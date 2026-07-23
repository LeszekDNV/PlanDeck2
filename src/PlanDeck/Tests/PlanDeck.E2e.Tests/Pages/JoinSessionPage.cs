using Microsoft.Playwright;

namespace PlanDeck.E2e.Tests.Pages;

public class JoinSessionPage
{
    private readonly IPage _page;
    private readonly string _baseUrl;

    public JoinSessionPage(IPage page, string baseUrl)
    {
        _page = page;
        _baseUrl = baseUrl;
    }

    public ILocator NameField => _page.GetByLabel("Your name", new() { Exact = true });

    public ILocator SubmitButton =>
        _page.GetByRole(AriaRole.Button, new() { Name = "Join", Exact = true });

    public ILocator ErrorAlert => _page.GetByTestId("join-error");

    public ILocator LoginMicrosoftButton =>
        _page.GetByRole(AriaRole.Button, new() { Name = "Sign in with a Microsoft account", Exact = true });

    public async Task GotoAsync(string code)
    {
        await _page.GotoAsync(
            $"{_baseUrl.TrimEnd('/')}/join/{code}",
            new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 120_000 });

        // Wait for the WASM app to boot and the join form to render.
        await SubmitButton.WaitForAsync(new()
        {
            State = WaitForSelectorState.Visible,
            Timeout = 60_000
        });
    }

    public async Task SubmitNameAsync(string name)
    {
        await NameField.FillAsync(name);
        await SubmitButton.ClickAsync();
    }
}
