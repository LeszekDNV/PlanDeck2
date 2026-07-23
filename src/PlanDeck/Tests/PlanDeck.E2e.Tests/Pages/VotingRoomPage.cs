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

    public ILocator Participants =>
        _page.GetByTestId("notvoted-status")
            .Or(_page.GetByTestId("voted-status"))
            .Or(_page.GetByTestId("revealed-vote"));

    public ILocator Participant(string displayName) =>
        Participants.Filter(new() { HasText = displayName });

    public ILocator VotedStatuses => _page.GetByTestId("voted-status");

    public ILocator RevealedVotes => _page.GetByTestId("revealed-vote");

    public ILocator AgreedEstimate => _page.GetByTestId("agreed-estimate");

    public ILocator TaskListItems => _page.GetByTestId("voting-task");

    public ILocator VoteButton(string value) => _page.GetByTestId($"vote-{value}");

    public ILocator RevealButton => _page.GetByTestId("reveal");

    public ILocator ResetButton => _page.GetByTestId("reset");

    public ILocator TaskListItem(string title) =>
        TaskListItems.Filter(new() { HasText = title });

    public ILocator TaskDescription => _page.GetByTestId("task-description");

    public ILocator DescriptionToggle =>
        _page.GetByRole(AriaRole.Button, new() { Name = "Description" });

    public async Task GotoAsync(Guid sessionId)
    {
        await _page.GotoAsync(
            $"{_baseUrl.TrimEnd('/')}/voting/{sessionId}",
            new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 120_000 });

        await WaitForLoadedAsync();
    }

    public async Task WaitForLoadedAsync()
    {
        await _page.GetByRole(AriaRole.Button, new() { Name = "Back to sessions", Exact = true })
            .WaitForAsync(new()
            {
                State = WaitForSelectorState.Visible,
                Timeout = 60_000
            });
    }

    public async Task SelectTaskAsync(string taskTitle) =>
        await TaskListItems.Filter(new() { HasText = taskTitle })
            .ClickAsync();

    public async Task VoteAsync(string value) =>
        await VoteButton(value).ClickAsync();

    public async Task RevealAsync() =>
        await RevealButton.ClickAsync();

    public async Task PickEstimateAsync(string value) =>
        await _page.GetByTestId($"pick-{value}").ClickAsync();
}

