using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace PlanDeck.E2e.Tests.Pages;

public class ProjectDetailsPage
{
    private readonly IPage _page;
    private readonly string _baseUrl;

    public ProjectDetailsPage(IPage page, string baseUrl)
    {
        _page = page;
        _baseUrl = baseUrl;
    }

    public ILocator SelectedProjectTitle => _page.GetByTestId("project-selected");

    public ILocator DeleteDialog =>
        _page.GetByRole(AriaRole.Dialog).Filter(new() { HasText = "Delete project" });

    public async Task GotoAsync(Guid projectId)
    {
        await _page.GotoAsync($"{_baseUrl.TrimEnd('/')}/projects/{projectId:D}", new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 120_000 });
        await SelectedProjectTitle.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 60_000 });
    }

    public async Task OpenSessionsAsync()
    {
        await _page.GetByRole(AriaRole.Link, new() { Name = "Open sessions", Exact = true }).ClickAsync();
        await _page.WaitForURLAsync(new Regex("/projects/[0-9a-fA-F-]{36}/sessions$"), new() { Timeout = 15_000 });
    }

    public async Task DeleteProjectAsync()
    {
        await OpenDeleteProjectDialogAsync();
        await ConfirmDeleteProjectAsync();
    }

    public async Task OpenDeleteProjectDialogAsync()
    {
        await _page.GetByRole(AriaRole.Button, new() { Name = "Delete project", Exact = true }).ClickAsync();
        await DeleteDialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
    }

    public async Task ConfirmDeleteProjectAsync()
    {
        await DeleteDialog.GetByRole(AriaRole.Button, new() { Name = "Delete project", Exact = true }).ClickAsync();
    }

    public async Task AssignTeamAsync(string teamName)
    {
        await _page.GetByRole(AriaRole.Combobox, new() { Name = "Team to assign", Exact = true }).ClickAsync();
        await _page.GetByRole(AriaRole.Option, new() { Name = teamName, Exact = true }).ClickAsync();
        await _page.GetByRole(AriaRole.Button, new() { Name = "Assign", Exact = true }).ClickAsync();
    }
}



