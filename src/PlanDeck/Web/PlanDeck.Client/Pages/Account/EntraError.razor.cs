using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using PlanDeck.Client.Resources;

namespace PlanDeck.Client.Pages.Account;

public partial class EntraError
{
    [SupplyParameterFromQuery(Name = "code")]
    private string? Code { get; set; }

    [SupplyParameterFromQuery(Name = "returnUrl")]
    private string? ReturnUrl { get; set; }

    private string? _message;
    private string? _returnUrl;

    protected override void OnInitialized()
    {
        _returnUrl = string.IsNullOrWhiteSpace(ReturnUrl) ? "/" : ReturnUrl;

        if (!string.IsNullOrWhiteSpace(Code))
        {
            var key = $"Account_EntraError_{Code}";
            var localized = Localizer[key];
            _message = localized.Value != $"[{key}]"
                ? localized.Value
                : Localizer["Account_EntraError_Generic"];
        }
        else
        {
            _message = Localizer["Account_EntraError_Generic"];
        }
    }
}