using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Localization;
using MudBlazor;
using PlanDeck.Client.Components.Account;
using PlanDeck.Client.Models;
using PlanDeck.Client.Resources;
using PlanDeck.Client.Services;

namespace PlanDeck.Client.Pages.Account;

public partial class ResetPassword
{
    [SupplyParameterFromQuery(Name = "email")]
    private string? Email { get; set; }

    [SupplyParameterFromQuery(Name = "token")]
    private string? Token { get; set; }

    private readonly ResetPasswordFormModel _model = new();
    private bool _isBusy;
    private bool _submitted;
    private string? _statusMessage;
    private IReadOnlyList<string> _errors = [];
    private Severity _statusSeverity = Severity.Info;

    protected override void OnInitialized()
    {
        if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(Token))
        {
            _statusSeverity = Severity.Error;
            _errors = [Localizer["Account_ResetPasswordFailed"]];
            _submitted = true;
        }
    }

    private async Task HandleSubmitAsync(EditContext context)
    {
        _isBusy = true;
        _statusMessage = null;
        _errors = [];

        try
        {
            if (!string.Equals(_model.NewPassword, _model.ConfirmPassword, StringComparison.Ordinal))
            {
                _errors = [Localizer["Account_PasswordsDoNotMatch"]];
                _statusSeverity = Severity.Error;
                return;
            }

            var result = await AccountService.ResetPasswordAsync(
                new ResetPasswordModel(Email!, Token!, _model.NewPassword));

            if (result.Status == "Success")
            {
                _submitted = true;
                _statusMessage = Localizer["Account_ResetPasswordSuccess"];
                _statusSeverity = Severity.Success;
            }
            else
            {
                _statusSeverity = Severity.Error;
                _errors = [Localizer["Account_ResetPasswordFailed"]];
            }
        }
        finally
        {
            _isBusy = false;
        }
    }

    private sealed class ResetPasswordFormModel
    {
        [Required(ErrorMessageResourceType = typeof(SharedResource), ErrorMessageResourceName = "Account_Password_Required")]
        [RegularExpression(@"^(?=.*[A-Z])(?=.*[a-z])(?=.*[0-9])(?=.*[^a-zA-Z0-9]).{12,}$", ErrorMessageResourceType = typeof(SharedResource), ErrorMessageResourceName = "Account_Password_Invalid")]
        [DataType(DataType.Password)]
        [Display(Name = "Account_NewPassword_Label")]
        public string NewPassword { get; set; } = string.Empty;

        [Required(ErrorMessageResourceType = typeof(SharedResource), ErrorMessageResourceName = "Account_ConfirmPassword_Required")]
        [DataType(DataType.Password)]
        [Display(Name = "Account_ConfirmPassword_Label")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}
