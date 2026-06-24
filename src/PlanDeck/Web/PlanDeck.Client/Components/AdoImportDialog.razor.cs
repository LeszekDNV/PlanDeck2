using Microsoft.AspNetCore.Components;
using MudBlazor;
using PlanDeck.Core.Shared.Contracts;

namespace PlanDeck.Client.Components;

public partial class AdoImportDialog
{
    private AdoImportPanel? _panel;
    private int _selectedCount;

    [CascadingParameter]
    public IMudDialogInstance MudDialog { get; set; } = default!;

    [Parameter]
    public IReadOnlyCollection<int> AlreadyPresentIds { get; set; } = [];

    private void OnCountChanged(int count) => _selectedCount = count;

    private void Add() => MudDialog.Close(DialogResult.Ok(_panel!.SelectedItems));

    private void Cancel() => MudDialog.Cancel();
}
