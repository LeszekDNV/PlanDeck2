using System.ComponentModel.DataAnnotations;
using Grpc.Core;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Localization;
using MudBlazor;
using PlanDeck.Client.Components.Account;
using PlanDeck.Client.Models;
using PlanDeck.Client.Resources;
using PlanDeck.Client.Services;

namespace PlanDeck.Client.Pages.Account;

public partial class Login
{
    [SupplyParameterFromQuery(Name = "returnUrl")]
    private string? ReturnUrl { get; set; }

    [SupplyParameterFromQuery(Name = "code")]
    private string? EntraErrorCode { get; set; }

    private readonly LoginFormModel _model = new();
    private bool _isBusy;
    private bool _microsoftAuthenticationAvailable;
    private string? _statusMessage;
    private IReadOnlyList<string> _errors = [];
    private Severity _statusSeverity = Severity.Info;

    protected override async Task OnInitializedAsync()
    {
        if (!string.IsNullOrWhiteSpace(EntraErrorCode))
        {
            _statusMessage = Localizer[$"Account_EntraError_{EntraErrorCode}"];
            _statusSeverity = Severity.Error;
        }

        try
        {
            _microsoftAuthenticationAvailable =
                await AccountService.IsMicrosoftAuthenticationAvailableAsync();
        }
        catch (RpcException)
        {
            _microsoftAuthenticationAvailable = false;
            if (string.IsNullOrWhiteSpace(EntraErrorCode))
            {
                _statusSeverity = Severity.Warning;
                _errors = [Localizer["Error_Generic"]];
            }
        }
    }

    private async Task HandleSubmitAsync(EditContext context)
    {
        _isBusy = true;
        _statusMessage = null;
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

            var result = await AccountService.LoginAsync(
                new LocalLoginModel(_model.Login, _model.Password, _model.RememberMe),
                ReturnUrl);

            if (result.Status == "Success")
            {
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
        if (string.IsNullOrWhiteSpace(_model.Login))
        {
            errors.Add(Localizer["Account_Login_Required"]);
        }

        if (string.IsNullOrWhiteSpace(_model.Password))
        {
            errors.Add(Localizer["Account_Password_Required"]);
        }

        return errors;
    }

    private void LoginWithEntraAsync()
    {
        AccountService.NavigateToEntraLogin(ReturnUrl);
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

    private sealed class LoginFormModel
    {
        public string Login { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public bool RememberMe { get; set; }
    }
}
