using Grpc.Core;
using MudBlazor;
using PlanDeck.Core.Shared.Contracts;

namespace PlanDeck.Client.Pages;

public partial class Projects
{
    private bool _loading = true;
    private bool _busy;
    private bool _isGuest;
    private bool _createOpen;
    private string? _operationError;
    private List<ProjectDto> _projects = [];

    private string _createName = string.Empty;
    private string? _createDescription;

    protected override async Task OnInitializedAsync()
    {
        var state = await AuthState.GetAuthenticationStateAsync();
        _isGuest = state.User.HasClaim("is_guest", "true");
        if (state.User.Identity?.IsAuthenticated == true && !_isGuest)
        {
            await LoadProjectsAsync();
        }

        _loading = false;
    }

    private async Task LoadProjectsAsync()
    {
        try
        {
            _projects = (await ProjectService.GetProjectsAsync()).ToList();
        }
        catch (RpcException ex)
        {
            ShowError(ex);
        }
    }

    private void OpenCreate()
    {
        _createName = string.Empty;
        _createDescription = null;
        _createOpen = true;
    }

    private async Task CreateProjectAsync()
    {
        _operationError = null;
        if (string.IsNullOrWhiteSpace(_createName))
        {
            Snackbar.Add(L["Projects_NameRequired"], Severity.Error);
            return;
        }

        _busy = true;
        try
        {
            var created = await ProjectService.CreateProjectAsync(_createName.Trim(), NormalizeOptional(_createDescription));
            _createOpen = false;
            await LoadProjectsAsync();
            OpenProject(created.Id);
        }
        catch (RpcException ex)
        {
            ShowError(ex);
        }
        finally
        {
            _busy = false;
        }
    }

    private void OpenProject(Guid projectId) => Navigation.NavigateTo($"/projects/{projectId:D}");

    private string RoleLabel(ProjectRoleDto role) => role switch
    {
        ProjectRoleDto.Owner => L["Projects_RoleOwner"],
        ProjectRoleDto.Admin => L["Projects_RoleAdmin"],
        _ => L["Projects_RoleMember"]
    };

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private void Login() =>
        Navigation.NavigateTo(
            $"/auth/login?returnUrl={Uri.EscapeDataString(Navigation.Uri)}",
            forceLoad: true);

    private void ShowError(RpcException ex)
    {
        var message = ex.StatusCode switch
        {
            StatusCode.InvalidArgument => L["Projects_ValidationError"],
            StatusCode.PermissionDenied => L["Projects_PermissionDenied"],
            StatusCode.NotFound => L["Projects_NotFound"],
            _ => L["Error_Generic"]
        };
        _operationError = message;
        Snackbar.Add(message, Severity.Error);
    }
}
