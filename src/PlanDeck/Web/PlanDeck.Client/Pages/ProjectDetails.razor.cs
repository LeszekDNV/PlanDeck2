using Grpc.Core;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using PlanDeck.Core.Shared.Contracts;

namespace PlanDeck.Client.Pages;

public partial class ProjectDetails
{
    [Parameter]
    public Guid ProjectId { get; set; }

    private bool _loading = true;
    private bool _busy;
    private bool _notFound;
    private string? _operationError;

    private List<TeamDto> _teams = [];
    private GetProjectReply? _selected;
    private List<TeamDto> _selectedTeams = [];

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

    protected override async Task OnParametersSetAsync()
    {
        _loading = true;
        _notFound = false;
        _operationError = null;
        _selected = null;

        if (ProjectId == Guid.Empty)
        {
            _notFound = true;
            _loading = false;
            return;
        }

        await LoadTeamsAsync();
        await SelectAsync(ProjectId);
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
        catch (RpcException ex) when (ex.StatusCode is StatusCode.NotFound or StatusCode.PermissionDenied)
        {
            _notFound = true;
        }
        catch (RpcException ex)
        {
            ShowError(ex);
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
            Navigation.NavigateTo("/projects");
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
    }

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
