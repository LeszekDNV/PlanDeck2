using Grpc.Core;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using PlanDeck.Core.Shared.Contracts;

namespace PlanDeck.Client.Components;

public partial class AdoImportPanel
{
    private static readonly string[] WorkItemTypes =
        ["User Story", "Product Backlog Item", "Bug", "Task"];

    private static readonly string[] States =
        ["New", "Active", "Resolved", "Closed", "Committed", "Done"];

    private IReadOnlyCollection<string> _selectedTypes = new List<string>();
    private IReadOnlyCollection<string> _selectedStates = new List<string>();
    private int _limit = 100;
    private bool _loading;
    private bool _loaded;
    private string _filter = string.Empty;
    private List<AzureDevOpsWorkItemDto> _items = [];
    private readonly HashSet<int> _selectedIds = [];

    [Parameter]
    public IReadOnlyCollection<int> AlreadyPresentIds { get; set; } = [];

    [Parameter]
    public EventCallback<int> SelectedCountChanged { get; set; }

    public IReadOnlyList<AzureDevOpsWorkItemDto> SelectedItems =>
        _items.Where(item => _selectedIds.Contains(item.Id)).ToList();

    private IEnumerable<AzureDevOpsWorkItemDto> FilteredItems
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_filter))
            {
                return _items;
            }

            var term = _filter.Trim();
            return _items.Where(item =>
                item.Id.ToString().Contains(term, StringComparison.OrdinalIgnoreCase)
                || item.Title.Contains(term, StringComparison.OrdinalIgnoreCase));
        }
    }

    private async Task LoadAsync()
    {
        _loading = true;
        try
        {
            _items = (await AdoService.ImportWorkItemsAsync(
                _selectedTypes.ToArray(),
                _selectedStates.ToArray(),
                _limit)).ToList();
            _selectedIds.Clear();
            _filter = string.Empty;
            _loaded = true;
            await SelectedCountChanged.InvokeAsync(0);
        }
        catch (RpcException)
        {
            _items = [];
            _selectedIds.Clear();
            _loaded = true;
            await SelectedCountChanged.InvokeAsync(0);
            Snackbar.Add(L["Error_Generic"], Severity.Error);
        }
        finally
        {
            _loading = false;
        }
    }

    private async Task Toggle(int id, bool selected)
    {
        if (selected)
        {
            _selectedIds.Add(id);
        }
        else
        {
            _selectedIds.Remove(id);
        }

        await SelectedCountChanged.InvokeAsync(_selectedIds.Count);
    }
}
