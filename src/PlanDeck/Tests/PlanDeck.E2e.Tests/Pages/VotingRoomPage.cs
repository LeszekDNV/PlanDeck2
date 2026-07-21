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

    public ILocator Participant(string displayName) =>
        Participants.Filter(new() { HasText = displayName });

    public ILocator VotedStatuses => _page.Locator("[data-testid=voted-status]");

    public ILocator RevealedVotes => _page.Locator("[data-testid=revealed-vote]");

    public ILocator AgreedEstimate => _page.Locator("[data-testid=agreed-estimate]");

    public ILocator TaskListItems => _page.Locator("[data-testid=voting-task]");

    public ILocator VoteButton(string value) => _page.Locator($"[data-testid='vote-{value}']");

    public ILocator RevealButton => _page.Locator("[data-testid=reveal]");

    public ILocator ResetButton => _page.Locator("[data-testid=reset]");

    public ILocator TaskListItem(string title) =>
        TaskListItems.Filter(new() { HasText = title });

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
        await TaskListItems.Filter(new() { HasText = taskTitle })
            .First
            .ClickAsync();

    public async Task VoteAsync(string value) =>
        await VoteButton(value).ClickAsync();

    public async Task RevealAsync() =>
        await _page.Locator("[data-testid=reveal]").ClickAsync();

    public async Task PickEstimateAsync(string value) =>
        await _page.Locator($"[data-testid='pick-{value}']").ClickAsync();
}
