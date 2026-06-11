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

    private ILocator CallServerButton =>
        _page.GetByRole(AriaRole.Button, new() { Name = "Call server" });

    public ILocator ServerResponse =>
        _page.GetByText("Hello World!", new() { Exact = true });

    public async Task GotoAsync()
    {
        await _page.GotoAsync(_baseUrl, new() { WaitUntil = WaitUntilState.NetworkIdle });

        // Wait for the Blazor WebAssembly app to finish booting and render the button.
        await CallServerButton.WaitForAsync(new()
        {
            State = WaitForSelectorState.Visible,
            Timeout = 60_000
        });
    }

    public Task ClickCallServerAsync() => CallServerButton.ClickAsync();
}
