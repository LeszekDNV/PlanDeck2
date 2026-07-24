using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using MudBlazor;
using PlanDeck.Client.Resources;

namespace PlanDeck.Client.Components.Account;

public partial class PasswordValidationHints
{
    [Inject]
    private IStringLocalizer<SharedResource> Localizer { get; set; } = null!;

    [Parameter]
    public string? Password { get; set; }

    private bool HasLength => !string.IsNullOrEmpty(Password) && Password.Length >= 12;
    private bool HasUpper => !string.IsNullOrEmpty(Password) && Password.Any(char.IsUpper);
    private bool HasLower => !string.IsNullOrEmpty(Password) && Password.Any(char.IsLower);
    private bool HasDigit => !string.IsNullOrEmpty(Password) && Password.Any(char.IsDigit);
    private bool HasSpecial => !string.IsNullOrEmpty(Password) && Password.Any(c => !char.IsLetterOrDigit(c));

    private string GetIcon(bool valid) => valid
        ? Icons.Material.Filled.CheckCircle
        : Icons.Material.Outlined.Circle;
}
