using Grpc.Core;
using MudBlazor;
using PlanDeck.Core.Shared.Contracts;
using PlanDeck.Core.Shared.Validation;

namespace PlanDeck.Client.Pages;

public partial class Teams
{
    private bool _loading = true;
    private bool _membersLoading;
    private bool _createOpen;
    private bool _createSubmitting;
    private bool _memberSubmitting;

    private List<TeamDto> _teams = [];
    private List<TeamMemberDto> _members = [];
    private TeamDto? _selectedTeam;

    private string _newTeamName = string.Empty;
    private string? _newTeamDescription;
    private string _newMemberEmail = string.Empty;
    private string? _newMemberDisplayName;

    protected override async Task OnInitializedAsync()
    {
        var state = await AuthState.GetAuthenticationStateAsync();
        if (state.User.Identity?.IsAuthenticated == true)
        {
            await LoadTeamsAsync();
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

    private void OpenCreate()
    {
        _newTeamName = string.Empty;
        _newTeamDescription = null;
        _createOpen = true;
    }

    private async Task SubmitCreateAsync()
    {
        if (string.IsNullOrWhiteSpace(_newTeamName))
        {
            Snackbar.Add(L["Teams_NameRequired"], Severity.Error);
            return;
        }

        _createSubmitting = true;
        try
        {
            var team = await TeamService.CreateTeamAsync(_newTeamName.Trim(), _newTeamDescription);
            _teams.Add(team);
            _createOpen = false;
            await SelectTeamAsync(team);
        }
        catch (RpcException ex)
        {
            ShowError(ex);
        }
        finally
        {
            _createSubmitting = false;
        }
    }

    private async Task SelectTeamAsync(TeamDto team)
    {
        _selectedTeam = team;
        await LoadMembersAsync();
    }

    private async Task LoadMembersAsync()
    {
        if (_selectedTeam is null)
        {
            return;
        }

        _membersLoading = true;
        try
        {
            _members = (await TeamService.GetMembersAsync(_selectedTeam.Id)).ToList();
        }
        catch (RpcException ex)
        {
            ShowError(ex);
        }
        finally
        {
            _membersLoading = false;
        }
    }

    private async Task AddMemberAsync()
    {
        if (_selectedTeam is null)
        {
            return;
        }

        var email = _newMemberEmail?.Trim() ?? string.Empty;
        if (!EmailValidator.IsValid(email))
        {
            Snackbar.Add(L["Members_EmailRequired"], Severity.Error);
            return;
        }

        _memberSubmitting = true;
        try
        {
            var member = await TeamService.AddMemberAsync(
                _selectedTeam.Id,
                email,
                _newMemberDisplayName);
            _members.Add(member);
            _newMemberEmail = string.Empty;
            _newMemberDisplayName = null;
        }
        catch (RpcException ex)
        {
            ShowError(ex);
        }
        finally
        {
            _memberSubmitting = false;
        }
    }

    private async Task RemoveMemberAsync(TeamMemberDto member)
    {
        if (_selectedTeam is null)
        {
            return;
        }

        var confirmed = await Dialog.ShowMessageBoxAsync(
            L["Members_RemoveConfirmTitle"],
            string.Format(L["Members_RemoveConfirmText"], member.Email),
            yesText: L["Members_Remove"],
            cancelText: L["Teams_Cancel"]);

        if (confirmed != true)
        {
            return;
        }

        try
        {
            var removed = await TeamService.RemoveMemberAsync(_selectedTeam.Id, member.Id);
            if (removed)
            {
                _members.Remove(member);
            }
        }
        catch (RpcException ex)
        {
            ShowError(ex);
        }
    }

    private void Login() =>
        Navigation.NavigateTo(
            $"/auth/login?returnUrl={Uri.EscapeDataString(Navigation.Uri)}",
            forceLoad: true);

    private void ShowError(RpcException ex)
    {
        var message = ex.StatusCode switch
        {
            StatusCode.InvalidArgument => L["Members_EmailRequired"],
            StatusCode.AlreadyExists => L["Members_DuplicateError"],
            _ => L["Error_Generic"]
        };

        Snackbar.Add(message, Severity.Error);
    }
}
