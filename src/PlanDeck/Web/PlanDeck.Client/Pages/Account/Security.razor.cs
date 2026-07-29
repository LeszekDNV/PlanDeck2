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

public partial class Security
{
    private SecurityInfoModel? _info;
    private readonly LinkFormModel _linkModel = new();
    private bool _isBusy;
    private bool _microsoftAuthenticationAvailable;
    private bool _showLinkForm;
    private string? _statusMessage;
    private IReadOnlyList<string> _errors = [];
    private Severity _statusSeverity = Severity.Info;

    protected override async Task OnInitializedAsync()
    {
        await LoadSecurityInfoAsync();
    }

    private async Task LoadSecurityInfoAsync()
    {
        _isBusy = true;
        try
        {
            _info = await AccountService.GetSecurityInfoAsync();
            try
            {
                _microsoftAuthenticationAvailable =
                    await AccountService.IsMicrosoftAuthenticationAvailableAsync();
            }
            catch (RpcException)
            {
                _microsoftAuthenticationAvailable = false;
                _statusSeverity = Severity.Warning;
                _errors = [Localizer["Error_Generic"]];
            }
        }
        catch
        {
            _statusSeverity = Severity.Error;
            _errors = [Localizer["Error_Generic"]];
        }
        finally
        {
            _isBusy = false;
        }
    }

    private void ShowLinkForm()
    {
        _showLinkForm = true;
        _linkModel.Password = string.Empty;
    }

    private void HideLinkForm()
    {
        _showLinkForm = false;
    }

    private async Task HandleLinkAsync(EditContext context)
    {
        _isBusy = true;
        _statusMessage = null;
        _errors = [];

        try
        {
            var result = await AccountService.LinkEntraAsync(
                new LinkEntraModel(_linkModel.Password));

            if (result.Succeeded || result.Status == "ChallengeIssued")
            {
                return;
            }

            _statusSeverity = Severity.Error;
            _errors = MapErrors(result);
            _showLinkForm = false;
        }
        finally
        {
            _isBusy = false;
        }
    }

    private async Task UnlinkAsync(LinkedLoginInfoModel login)
    {
        _isBusy = true;
        _statusMessage = null;
        _errors = [];

        try
        {
            var result = await AccountService.UnlinkEntraAsync(
                new UnlinkEntraModel(login.Provider, string.Empty));

            if (result.Succeeded)
            {
                await LoadSecurityInfoAsync();
                _statusMessage = Localizer["Account_UnlinkSuccess"];
                _statusSeverity = Severity.Success;
            }
            else
            {
                _statusSeverity = Severity.Error;
                _errors = MapErrors(result);
            }
        }
        finally
        {
            _isBusy = false;
        }
    }

    private string GetConfirmationIcon() =>
        _info?.EmailConfirmed == true
            ? Icons.Material.Filled.CheckCircle
            : Icons.Material.Filled.Warning;

    private string GetProviderDisplayName(LinkedLoginInfoModel login)
    {
        if (!string.IsNullOrWhiteSpace(login.DisplayName))
        {
            return login.DisplayName;
        }

        var key = $"Account_Provider_{login.Provider}";
        var localized = Localizer[key];
        return !localized.ResourceNotFound ? localized.Value : login.Provider;
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

    private sealed class LinkFormModel
    {
        [Required(ErrorMessageResourceType = typeof(SharedResource), ErrorMessageResourceName = "Account_Password_Required")]
        [DataType(DataType.Password)]
        [Display(Name = "Account_Password_Label")]
        public string Password { get; set; } = string.Empty;
    }
}
