using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Localization;
using MudBlazor;
using PlanDeck.Client.Components.Account;
using PlanDeck.Client.Resources;
using PlanDeck.Client.Services;

namespace PlanDeck.Client.Pages.Account;

public partial class ForgotPassword
{
    private readonly ForgotPasswordFormModel _model = new();
    private bool _isBusy;
    private bool _submitted;
    private string? _statusMessage;
    private IReadOnlyList<string> _errors = [];
    private Severity _statusSeverity = Severity.Info;

    private async Task HandleSubmitAsync(EditContext context)
    {
        _isBusy = true;
        _statusMessage = null;
        _errors = [];

        try
        {
            var result = await AccountService.ForgotPasswordAsync(_model.Email);
            _submitted = true;
            _statusMessage = result.Status == "Sent"
                ? Localizer["Account_ForgotPasswordSent"]
                : Localizer["Account_ForgotPasswordFailed"];
            _statusSeverity = result.Status == "Sent" ? Severity.Success : Severity.Error;
        }
        finally
        {
            _isBusy = false;
        }
    }

    private sealed class ForgotPasswordFormModel
    {
        [Required(ErrorMessageResourceType = typeof(SharedResource), ErrorMessageResourceName = "Account_Email_Required")]
        [EmailAddress(ErrorMessageResourceType = typeof(SharedResource), ErrorMessageResourceName = "Account_Email_Invalid")]
        [Display(Name = "Account_Email_Label")]
        public string Email { get; set; } = string.Empty;
    }
}
