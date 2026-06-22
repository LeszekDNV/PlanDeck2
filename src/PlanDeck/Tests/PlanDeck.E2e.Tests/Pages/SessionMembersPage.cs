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
        await MemberRow(email)
            .GetByRole(AriaRole.Button, new() { Name = "Remove member" })
            .ClickAsync();

        // Confirm the removal in the MudMessageBox dialog.
        await _page.Locator(".mud-dialog")
            .GetByRole(AriaRole.Button, new() { Name = "Remove member" })
            .ClickAsync();
    }

    public ILocator MemberEntry(string email) => _page.GetByText(email);

    private ILocator MemberRow(string email) =>
        _page.Locator(".mud-list-item").Filter(new() { HasText = email });
}
