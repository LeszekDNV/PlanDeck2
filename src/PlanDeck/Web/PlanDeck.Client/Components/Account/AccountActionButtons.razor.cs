using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using PlanDeck.Client.Resources;

namespace PlanDeck.Client.Components.Account;

public partial class AccountActionButtons
{
    [Inject]
    private IStringLocalizer<SharedResource> Localizer { get; set; } = null!;

    [Parameter]
    public string PrimaryText { get; set; } = string.Empty;

    [Parameter]
    public string EntraText { get; set; } = string.Empty;

    [Parameter]
    public bool ShowEntra { get; set; } = true;

    [Parameter]
    public bool IsBusy { get; set; }

    [Parameter]
    public EventCallback OnEntraClick { get; set; }
}
