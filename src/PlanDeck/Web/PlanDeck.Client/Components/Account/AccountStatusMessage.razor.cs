using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace PlanDeck.Client.Components.Account;

public partial class AccountStatusMessage
{
    [Parameter]
    public string? Status { get; set; }

    [Parameter]
    public IReadOnlyList<string> Errors { get; set; } = [];

    [Parameter]
    public Severity Severity { get; set; } = Severity.Info;
}
