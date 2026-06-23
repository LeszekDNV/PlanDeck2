using Microsoft.Playwright;

namespace PlanDeck.E2e.Tests.Pages;

public class VotingRoomPage
{
    private readonly IPage _page;
    private readonly string _baseUrl;

    public VotingRoomPage(IPage page, string baseUrl)
    {
        _page = page;
        _baseUrl = baseUrl;
    }

    public ILocator Participants => _page.Locator("[data-testid=participant]");

    public ILocator VotedStatuses => _page.Locator("[data-testid=voted-status]");

    public ILocator RevealedVotes => _page.Locator("[data-testid=revealed-vote]");

    public ILocator AgreedEstimate => _page.Locator("[data-testid=agreed-estimate]");

    public ILocator VoteButton(string value) => _page.Locator($"[data-testid='vote-{value}']");

    public ILocator TaskListItem(string title) =>
        _page.Locator(".mud-list-item").Filter(new() { HasText = title });

    public ILocator TaskDescription => _page.Locator("[data-testid=task-description]");

    public ILocator DescriptionToggle =>
        _page.GetByRole(AriaRole.Button, new() { Name = "Description" });

    public async Task GotoAsync(Guid sessionId)
    {
        await _page.GotoAsync(
            $"{_baseUrl.TrimEnd('/')}/voting/{sessionId}",
            new() { WaitUntil = WaitUntilState.NetworkIdle });

        await WaitForLoadedAsync();
    }

    public async Task WaitForLoadedAsync() =>
        // The WASM app boots, then the room state arrives and the roster renders the self entry.
        await Participants.First.WaitForAsync(new()
        {
            State = WaitForSelectorState.Visible,
            Timeout = 60_000
        });

    public async Task SelectTaskAsync(string taskTitle) =>
        await _page.Locator(".mud-list-item")
            .Filter(new() { HasText = taskTitle })
            .First
            .ClickAsync();

    public async Task VoteAsync(string value) =>
        await VoteButton(value).ClickAsync();

    public async Task RevealAsync() =>
        await _page.Locator("[data-testid=reveal]").ClickAsync();

    public async Task PickEstimateAsync(string value) =>
        await _page.Locator($"[data-testid='pick-{value}']").ClickAsync();
}
