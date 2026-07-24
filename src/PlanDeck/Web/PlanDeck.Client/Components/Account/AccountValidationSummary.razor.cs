using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Localization;
using MudBlazor;
using PlanDeck.Client.Resources;

namespace PlanDeck.Client.Components.Account;

public partial class AccountValidationSummary : ComponentBase, IDisposable
{
    [CascadingParameter]
    private EditContext? EditContext { get; set; }

    private bool _hasErrors;

    protected override void OnInitialized()
    {
        if (EditContext is null)
        {
            return;
        }

        _hasErrors = EditContext.GetValidationMessages().Any();
        EditContext.OnValidationStateChanged += HandleValidationStateChanged;
    }

    private void HandleValidationStateChanged(object? sender, ValidationStateChangedEventArgs e)
    {
        _hasErrors = EditContext?.GetValidationMessages().Any() ?? false;
        StateHasChanged();
    }

    public void Dispose()
    {
        if (EditContext is not null)
        {
            EditContext.OnValidationStateChanged -= HandleValidationStateChanged;
        }
    }
}