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

public partial class Register
{
    [SupplyParameterFromQuery(Name = "invitationToken")]
    private string? InvitationToken { get; set; }

    [SupplyParameterFromQuery(Name = "returnUrl")]
    private string? ReturnUrl { get; set; }

    private readonly RegisterFormModel _model = new();
    private bool _isBusy;
    private string? _statusMessage;
    private string? _passwordError;
    private IReadOnlyList<string> _errors = [];
    private Severity _statusSeverity = Severity.Info;

    private async Task HandleSubmitAsync(EditContext context)
    {
        _isBusy = true;
        _statusMessage = null;
        _passwordError = null;
        _errors = [];

        try
        {
            var validationErrors = ValidateForm();
            if (validationErrors.Count > 0)
            {
                _statusSeverity = Severity.Error;
                _errors = validationErrors;
                return;
            }

            var result = await AccountService.RegisterAsync(
                new LocalRegisterModel(
                    _model.Email,
                    _model.FirstName ?? string.Empty,
                    _model.LastName ?? string.Empty,
                    _model.UserName,
                    _model.Password,
                    InvitationToken));

            if (result.Succeeded)
            {
                Navigation.NavigateTo(
                    $"/account/confirm-email?email={Uri.EscapeDataString(_model.Email)}&mode=registered",
                    forceLoad: true);
                return;
            }

            _statusSeverity = Severity.Error;
            _errors = MapErrors(result);
        }
        finally
        {
            _isBusy = false;
        }
    }

    private IReadOnlyList<string> ValidateForm()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(_model.Email))
        {
            errors.Add(Localizer["Account_Email_Required"]);
        }
        else if (!new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(_model.Email))
        {
            errors.Add(Localizer["Account_Email_Invalid"]);
        }

        if (string.IsNullOrWhiteSpace(_model.UserName))
        {
            errors.Add(Localizer["Account_UserName_Required"]);
        }
        else
        {
            if (_model.UserName.Length < 3)
            {
                errors.Add(Localizer["Account_UserName_TooShort"]);
            }

            if (_model.UserName.Length > 32)
            {
                errors.Add(Localizer["Account_UserName_TooLong"]);
            }

            if (_model.UserName.Contains('@'))
            {
                errors.Add(Localizer["Account_UserName_NoAt"]);
            }
        }

        if (string.IsNullOrWhiteSpace(_model.Password))
        {
            errors.Add(Localizer["Account_Password_Required"]);
        }
        else if (!PasswordRegex().IsMatch(_model.Password))
        {
            _passwordError = Localizer["Account_Password_Invalid"];
            errors.Add(_passwordError);
        }

        if (string.IsNullOrWhiteSpace(_model.ConfirmPassword))
        {
            errors.Add(Localizer["Account_ConfirmPassword_Required"]);
        }
        else if (!string.Equals(_model.Password, _model.ConfirmPassword, StringComparison.Ordinal))
        {
            errors.Add(Localizer["Account_PasswordsDoNotMatch"]);
        }

        return errors;
    }

    private void RegisterWithEntraAsync()
    {
        AccountService.NavigateToEntraRegister(ReturnUrl, InvitationToken);
    }

    private IReadOnlyList<string> MapErrors(AccountActionResponse result)
    {
        var key = $"Account_Error_{result.Status}";
        var localized = Localizer[key];
        if (!localized.ResourceNotFound)
        {
            return [localized.Value];
        }

        if (result.Errors?.Count > 0)
        {
            return result.Errors.ToList();
        }

        return [Localizer["Error_Generic"]];
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"^(?=.*[A-Z])(?=.*[a-z])(?=.*[0-9])(?=.*[^a-zA-Z0-9]).{12,}$")]
    private static partial System.Text.RegularExpressions.Regex PasswordRegex();

    private sealed class RegisterFormModel
    {
        public string Email { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string Password { get; set; } = string.Empty;
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}
