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
    private List<TeamDto> _teams = [];
    private GetProjectReply? _selected;
    private List<TeamDto> _selectedTeams = [];

    private string _createName = string.Empty;
    private string? _createDescription;
    private string _createOrganizationUrl = string.Empty;
    private string _createAdoProject = string.Empty;
    private string _createEstimateField = "Microsoft.VSTS.Scheduling.StoryPoints";
    private string _createDescriptionField = "System.Description";
    private string _createReproStepsField = "Microsoft.VSTS.TCM.ReproSteps";
    private string _createAcceptanceCriteriaField = "Microsoft.VSTS.Common.AcceptanceCriteria";
    private string _createPat = string.Empty;

    private string _connectionOrganizationUrl = string.Empty;
    private string _connectionProject = string.Empty;
    private string _connectionEstimateField = "Microsoft.VSTS.Scheduling.StoryPoints";
    private string _connectionDescriptionField = "System.Description";
    private string _connectionReproStepsField = "Microsoft.VSTS.TCM.ReproSteps";
    private string _connectionAcceptanceCriteriaField = "Microsoft.VSTS.Common.AcceptanceCriteria";
    private string _connectionPat = string.Empty;

    private string _inviteEmail = string.Empty;
    private ProjectRoleDto _inviteRole = ProjectRoleDto.Member;
    private Guid _newOwnerMemberId;
    private Guid _teamToAssign;

    private bool IsOwner => _selected?.Project.EffectiveRole == ProjectRoleDto.Owner;

    private bool CanManageMembers =>
        _selected is not null
        && (_selected.Project.EffectiveRole == ProjectRoleDto.Owner
            || _selected.Project.EffectiveRole == ProjectRoleDto.Admin);

    private IEnumerable<ProjectMemberDto> OwnershipCandidates =>
        _selected?.Members
            .Where(member => member.Role != ProjectRoleDto.Owner && member.Status == InvitationStatusDto.Accepted)
        ?? [];

    private IEnumerable<TeamDto> AssignableTeams =>
        _teams.Where(team => _selectedTeams.All(assigned => assigned.Id != team.Id));

    protected override async Task OnInitializedAsync()
    {
        var state = await AuthState.GetAuthenticationStateAsync();
        _isGuest = state.User.HasClaim("is_guest", "true");
        if (state.User.Identity?.IsAuthenticated == true && !_isGuest)
        {
            await LoadTeamsAsync();
            await LoadProjectsAsync();
        }

        _loading = false;
    }

    private async Task LoadTeamsAsync()
    {
        try
        {
            _teams = (await TeamService.GetTeamsAsync()).ToList();
        }
        catch (RpcException ex)
        {
            ShowError(ex);
        }
    }

    private async Task LoadProjectsAsync()
    {
        try
        {
            _projects = (await ProjectService.GetProjectsAsync()).ToList();
            if (_selected is not null && _projects.All(project => project.Id != _selected.Project.Id))
            {
                _selected = null;
                _selectedTeams = [];
            }
        }
        catch (RpcException ex)
        {
            ShowError(ex);
        }
    }

    private async Task SelectAsync(Guid projectId)
    {
        try
        {
            _selected = await ProjectService.GetProjectAsync(projectId);
            _selectedTeams = _teams
                .Where(team => _selected.Teams.Any(projectTeam => projectTeam.TeamId == team.Id))
                .OrderBy(team => team.Name)
                .ToList();
            _newOwnerMemberId = OwnershipCandidates.Select(member => member.Id).FirstOrDefault();
            _teamToAssign = AssignableTeams.Select(team => team.Id).FirstOrDefault();
            LoadConnectionFieldsFromSelection();
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
        _createOrganizationUrl = string.Empty;
        _createAdoProject = string.Empty;
        _createEstimateField = "Microsoft.VSTS.Scheduling.StoryPoints";
        _createDescriptionField = "System.Description";
        _createReproStepsField = "Microsoft.VSTS.TCM.ReproSteps";
        _createAcceptanceCriteriaField = "Microsoft.VSTS.Common.AcceptanceCriteria";
        _createPat = string.Empty;
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
            if (!string.IsNullOrWhiteSpace(_createOrganizationUrl)
                && !string.IsNullOrWhiteSpace(_createAdoProject)
                && !string.IsNullOrWhiteSpace(_createPat))
            {
                await ProjectService.ConfigureConnectionAsync(
                    created.Id,
                    _createOrganizationUrl.Trim(),
                    _createAdoProject.Trim(),
                    _createEstimateField.Trim(),
                    _createDescriptionField.Trim(),
                    _createReproStepsField.Trim(),
                    _createAcceptanceCriteriaField.Trim(),
                    _createPat.Trim());
            }

            _createOpen = false;
            await LoadProjectsAsync();
            await SelectAsync(created.Id);
        }
        catch (RpcException ex)
        {
            ShowError(ex);
        }
        finally
        {
            _createPat = string.Empty;
            _busy = false;
        }
    }

    private async Task InviteMemberAsync()
    {
        if (_selected is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_inviteEmail))
        {
            Snackbar.Add(L["Projects_InviteEmailRequired"], Severity.Error);
            return;
        }

        _busy = true;
        try
        {
            await ProjectService.InviteMemberAsync(_selected.Project.Id, _inviteEmail.Trim(), _inviteRole);
            _inviteEmail = string.Empty;
            await ReloadSelectedAsync();
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

    private bool CanManageRole(ProjectMemberDto member) =>
        CanManageMembers && member.Role != ProjectRoleDto.Owner;

    private async Task ChangeMemberRoleAsync(ProjectMemberDto member, ProjectRoleDto role)
    {
        if (_selected is null || !CanManageRole(member))
        {
            return;
        }

        _busy = true;
        try
        {
            await ProjectService.ChangeMemberRoleAsync(_selected.Project.Id, member.Id, role);
            await ReloadSelectedAsync();
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

    private async Task RemoveMemberAsync(ProjectMemberDto member)
    {
        if (_selected is null || !CanManageRole(member))
        {
            return;
        }

        var confirmed = await Dialog.ShowMessageBoxAsync(
            L["Projects_RemoveMemberTitle"],
            string.Format(L["Projects_RemoveMemberConfirm"], member.Email),
            yesText: L["Projects_RemoveMember"],
            cancelText: L["Teams_Cancel"]);

        if (confirmed != true)
        {
            return;
        }

        _busy = true;
        try
        {
            await ProjectService.RemoveMemberAsync(_selected.Project.Id, member.Id);
            await ReloadSelectedAsync();
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

    private async Task TransferOwnershipAsync()
    {
        if (_selected is null || _newOwnerMemberId == Guid.Empty)
        {
            return;
        }

        var confirmed = await Dialog.ShowMessageBoxAsync(
            L["Projects_TransferOwnership"],
            L["Projects_TransferOwnershipConfirm"],
            yesText: L["Projects_TransferOwnership"],
            cancelText: L["Teams_Cancel"]);
        if (confirmed != true)
        {
            return;
        }

        _busy = true;
        try
        {
            await ProjectService.TransferOwnershipAsync(_selected.Project.Id, _newOwnerMemberId);
            await ReloadSelectedAsync();
            await LoadProjectsAsync();
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

    private async Task AssignTeamAsync()
    {
        if (_selected is null || _teamToAssign == Guid.Empty)
        {
            return;
        }

        _busy = true;
        try
        {
            await ProjectService.AssignTeamAsync(_selected.Project.Id, _teamToAssign);
            await ReloadSelectedAsync();
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

    private async Task UnassignTeamAsync(Guid teamId)
    {
        if (_selected is null)
        {
            return;
        }

        _busy = true;
        try
        {
            await ProjectService.UnassignTeamAsync(_selected.Project.Id, teamId);
            await ReloadSelectedAsync();
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

    private async Task ConfigureConnectionAsync()
    {
        if (_selected is null)
        {
            return;
        }

        _busy = true;
        try
        {
            await ProjectService.ConfigureConnectionAsync(
                _selected.Project.Id,
                _connectionOrganizationUrl.Trim(),
                _connectionProject.Trim(),
                _connectionEstimateField.Trim(),
                _connectionDescriptionField.Trim(),
                _connectionReproStepsField.Trim(),
                _connectionAcceptanceCriteriaField.Trim(),
                _connectionPat.Trim());
            await ReloadSelectedAsync();
        }
        catch (RpcException ex)
        {
            ShowError(ex);
        }
        finally
        {
            _connectionPat = string.Empty;
            _busy = false;
        }
    }

    private async Task UpdateConnectionAsync()
    {
        if (_selected is null)
        {
            return;
        }

        _busy = true;
        try
        {
            await ProjectService.UpdateConnectionAsync(
                _selected.Project.Id,
                _connectionOrganizationUrl.Trim(),
                _connectionProject.Trim(),
                _connectionEstimateField.Trim(),
                _connectionDescriptionField.Trim(),
                _connectionReproStepsField.Trim(),
                _connectionAcceptanceCriteriaField.Trim());
            await ReloadSelectedAsync();
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

    private async Task ToggleConnectionAsync()
    {
        if (_selected?.Connection is null)
        {
            return;
        }

        _busy = true;
        try
        {
            await ProjectService.SetConnectionEnabledAsync(
                _selected.Project.Id,
                !_selected.Connection.IsEnabled);
            await ReloadSelectedAsync();
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

    private async Task RotateConnectionPatAsync()
    {
        if (_selected is null || string.IsNullOrWhiteSpace(_connectionPat))
        {
            Snackbar.Add(L["Projects_PatRequired"], Severity.Error);
            return;
        }

        _busy = true;
        try
        {
            await ProjectService.RotateConnectionPatAsync(_selected.Project.Id, _connectionPat.Trim());
            await ReloadSelectedAsync();
        }
        catch (RpcException ex)
        {
            ShowError(ex);
        }
        finally
        {
            _connectionPat = string.Empty;
            _busy = false;
        }
    }

    private async Task RemoveConnectionAsync()
    {
        if (_selected is null)
        {
            return;
        }

        var confirmed = await Dialog.ShowMessageBoxAsync(
            L["Projects_ConnectionRemove"],
            L["Projects_ConnectionRemoveConfirm"],
            yesText: L["Projects_ConnectionRemove"],
            cancelText: L["Teams_Cancel"]);
        if (confirmed != true)
        {
            return;
        }

        _busy = true;
        try
        {
            await ProjectService.RemoveConnectionAsync(_selected.Project.Id);
            await ReloadSelectedAsync();
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

    private async Task DeleteProjectAsync()
    {
        if (_selected is null)
        {
            return;
        }

        var confirmed = await Dialog.ShowMessageBoxAsync(
            L["Projects_DeleteProject"],
            L["Projects_DeleteProjectConfirm"],
            yesText: L["Projects_DeleteProject"],
            cancelText: L["Teams_Cancel"]);
        if (confirmed != true)
        {
            return;
        }

        var projectId = _selected.Project.Id;
        _busy = true;
        try
        {
            await ProjectService.DeleteProjectAsync(projectId);
            _selected = null;
            _selectedTeams = [];
            await LoadProjectsAsync();
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

    private string RoleLabel(ProjectRoleDto role) => role switch
    {
        ProjectRoleDto.Owner => L["Projects_RoleOwner"],
        ProjectRoleDto.Admin => L["Projects_RoleAdmin"],
        _ => L["Projects_RoleMember"]
    };

    private string StatusLabel(InvitationStatusDto status) => status switch
    {
        InvitationStatusDto.Pending => L["Projects_StatusPending"],
        _ => L["Projects_StatusAccepted"]
    };

    private string ConnectionStateLabel()
    {
        if (_selected?.Connection is null)
        {
            return L["Projects_ConnectionNotConfigured"];
        }

        var enabledLabel = _selected.Connection.IsEnabled
            ? L["Projects_ConnectionEnabled"]
            : L["Projects_ConnectionDisabled"];
        var validationLabel = _selected.Connection.ValidationState switch
        {
            ProjectConnectionValidationStateDto.Valid => L["Projects_ConnectionValid"],
            ProjectConnectionValidationStateDto.Invalid => L["Projects_ConnectionInvalid"],
            _ => L["Projects_ConnectionNotValidated"]
        };

        return $"{enabledLabel} · {validationLabel}";
    }

    private void LoadConnectionFieldsFromSelection()
    {
        if (_selected is null)
        {
            return;
        }

        _connectionOrganizationUrl = string.Empty;
        _connectionProject = string.Empty;
        _connectionEstimateField = "Microsoft.VSTS.Scheduling.StoryPoints";
        _connectionDescriptionField = "System.Description";
        _connectionReproStepsField = "Microsoft.VSTS.TCM.ReproSteps";
        _connectionAcceptanceCriteriaField = "Microsoft.VSTS.Common.AcceptanceCriteria";
        _connectionPat = string.Empty;
    }

    private async Task ReloadSelectedAsync()
    {
        if (_selected is null)
        {
            return;
        }

        await SelectAsync(_selected.Project.Id);
        await LoadProjectsAsync();
    }

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
            StatusCode.FailedPrecondition => L["Projects_FailedPrecondition"],
            StatusCode.NotFound => L["Projects_NotFound"],
            _ => L["Error_Generic"]
        };
        _operationError = message;
        Snackbar.Add(message, Severity.Error);
    }
}
