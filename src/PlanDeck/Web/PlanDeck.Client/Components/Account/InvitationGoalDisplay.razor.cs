using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using PlanDeck.Client.Resources;

namespace PlanDeck.Client.Components.Account;

public partial class InvitationGoalDisplay
{
    [Inject]
    private IStringLocalizer<SharedResource> Localizer { get; set; } = null!;

    [Parameter]
    public string? InvitationToken { get; set; }
}
