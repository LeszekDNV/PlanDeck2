using Microsoft.Playwright;

namespace PlanDeck.E2e.Tests.Pages;

public class SessionMembersPage
{
    private readonly IPage _page;

    public SessionMembersPage(IPage page)
    {
        _page = page;
    }

    private ILocator EmailField =>
        _page.GetByLabel("Member email", new() { Exact = true });

    private ILocator AssignButton =>
        _page.GetByRole(AriaRole.Button, new() { Name = "Assign", Exact = true });

    public async Task AssignMemberAsync(string email)
    {
        await EmailField.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        await EmailField.FillAsync(email);
        await AssignButton.ClickAsync();
        await MemberEntry(email).WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
    }

    public async Task RemoveMemberAsync(string email)
    {
        await _page.GetByTestId("session-member")
            .Filter(new() { HasText = email })
            .GetByRole(AriaRole.Button, new() { Name = "Remove member", Exact = true })
            .ClickAsync();

        var dialog = _page.GetByRole(AriaRole.Dialog).Filter(new() { HasText = "Remove member" });
        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Remove member", Exact = true }).ClickAsync();
    }

    public ILocator MemberEntry(string email) => _page.GetByText(email, new() { Exact = false });
}



