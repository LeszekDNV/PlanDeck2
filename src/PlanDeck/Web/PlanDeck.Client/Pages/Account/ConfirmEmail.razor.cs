using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Localization;
using MudBlazor;
using PlanDeck.Client.Components.Account;
using PlanDeck.Client.Resources;
using PlanDeck.Client.Services;

namespace PlanDeck.Client.Pages.Account;

public partial class ConfirmEmail
{
    [SupplyParameterFromQuery(Name = "userId")]
    private Guid? UserId { get; set; }

    [SupplyParameterFromQuery(Name = "token")]
    private string? Token { get; set; }

    [SupplyParameterFromQuery(Name = "email")]
    private string? Email { get; set; }

    [SupplyParameterFromQuery(Name = "mode")]
    private string? Mode { get; set; }

    private readonly ResendFormModel _resendModel = new();
    private bool _isBusy;
    private bool _showResend;
    private string? _statusMessage;
    private IReadOnlyList<string> _errors = [];
    private Severity _statusSeverity = Severity.Info;

    protected override async Task OnInitializedAsync()
    {
        if (!string.IsNullOrWhiteSpace(Email))
        {
            _resendModel.Email = Email;
        }

        if (UserId.HasValue && !string.IsNullOrWhiteSpace(Token))
        {
            _isBusy = true;
            try
            {
                var result = await AccountService.ConfirmEmailAsync(UserId.Value, Token);
                if (result.Status is "Success" or "AlreadyConfirmed")
                {
                    _statusMessage = Localizer["Account_ConfirmationSuccess"];
                    _statusSeverity = Severity.Success;
                    _showResend = false;
                }
                else
                {
                    _statusSeverity = Severity.Error;
                    _errors = [Localizer["Account_ConfirmationFailed"]];
                    _showResend = true;
                }
            }
            finally
            {
                _isBusy = false;
            }
        }
        else if (Mode == "registered")
        {
            _statusMessage = Localizer["Account_Register_CheckEmail"];
            _showResend = true;
        }
        else
        {
            _statusSeverity = Severity.Error;
            _errors = [Localizer["Account_ConfirmationFailed"]];
            _showResend = true;
        }
    }

    private async Task HandleResendAsync(EditContext context)
    {
        _isBusy = true;
        _statusMessage = null;
        _errors = [];

        try
        {
            var result = await AccountService.ResendConfirmationAsync(_resendModel.Email);
            _statusMessage = result.Status == "Sent"
                ? Localizer["Account_ResendConfirmationSent"]
                : Localizer["Account_ResendConfirmationFailed"];
            _statusSeverity = result.Status == "Sent" ? Severity.Success : Severity.Error;
        }
        finally
        {
            _isBusy = false;
        }
    }

    private sealed class ResendFormModel
    {
        [Required(ErrorMessageResourceType = typeof(SharedResource), ErrorMessageResourceName = "Account_Email_Required")]
        [EmailAddress(ErrorMessageResourceType = typeof(SharedResource), ErrorMessageResourceName = "Account_Email_Invalid")]
        [Display(Name = "Account_Email_Label")]
        public string Email { get; set; } = string.Empty;
    }
}
