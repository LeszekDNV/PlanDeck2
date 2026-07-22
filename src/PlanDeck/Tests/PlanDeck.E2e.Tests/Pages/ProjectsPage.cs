using Microsoft.Playwright;

namespace PlanDeck.E2e.Tests.Pages;

public class ProjectsPage
{
    private readonly IPage _page;
    private readonly string _baseUrl;

    public ProjectsPage(IPage page, string baseUrl)
    {
        _page = page;
        _baseUrl = baseUrl;
    }

    private ILocator CreateProjectButton =>
        _page.GetByRole(AriaRole.Button, new() { Name = "Create project" });

    private ILocator CreateDialog =>
        _page.Locator(".mud-dialog").Filter(new() { HasText = "Create project" });

    private ILocator OperationError => _page.GetByTestId("project-operation-error");

    private ILocator SelectedProject => _page.GetByTestId("project-selected");

    public async Task GotoAsync()
    {
        await _page.GotoAsync($"{_baseUrl.TrimEnd('/')}/projects", new() { WaitUntil = WaitUntilState.NetworkIdle });
        await CreateProjectButton.WaitForAsync(new()
        {
            State = WaitForSelectorState.Visible,
            Timeout = 60_000
        });
    }

    public async Task<string> CreateUniqueProjectAsync(string prefix)
    {
        var name = $"{prefix} {Guid.NewGuid():N}";
        await GotoAsync();
        await CreateProjectAsync(name);
        return name;
    }

    public async Task CreateProjectAsync(string name, string? description = null)
    {
        await CreateProjectButton.ClickAsync();
        await CreateDialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });

        var nameField = _page.GetByTestId("project-name-input");
        await nameField.FillAsync(name);
        if (!string.IsNullOrWhiteSpace(description))
        {
            await CreateDialog.GetByLabel("Description", new() { Exact = true }).FillAsync(description);
        }

        await _page.GetByTestId("project-create-save").ClickAsync();
        await ProjectEntry(name)
            .Or(OperationError)
            .First
            .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        if (await OperationError.IsVisibleAsync())
        {
            throw new InvalidOperationException(
                $"Project creation failed: {await OperationError.InnerTextAsync()}");
        }

        await SelectedProject.Filter(new() { HasText = name }).WaitForAsync(new()
        {
            State = WaitForSelectorState.Visible,
            Timeout = 15_000
        });
        if (await CreateDialog.IsVisibleAsync())
        {
            await CreateDialog.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true }).ClickAsync();
        }
    }

    public ILocator ProjectEntry(string name) =>
        _page.Locator("[data-testid=project-entry]").Filter(new() { HasText = name });
}
