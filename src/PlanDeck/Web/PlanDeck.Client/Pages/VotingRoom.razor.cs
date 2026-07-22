using Grpc.Core;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR;
using MudBlazor;
using PlanDeck.Core.Shared.Contracts;
using PlanDeck.Core.Shared.Realtime;

namespace PlanDeck.Client.Pages;

public partial class VotingRoom : IAsyncDisposable
{
    [Parameter]
    public Guid SessionId { get; set; }

    private bool _loading = true;
    private bool _busy;
    private bool _descExpanded = true;
    private bool _isGuest;
    private string? _errorKey;
    private string? _myParticipantId;
    private string? _myVote;
    private Guid _projectId;
    private Guid? _activeTaskId;
    private PlanningRoomState? _state;

    private PlanningTaskState? _activeTask =>
        _state?.CurrentTaskId is { } id ? _state.Tasks.FirstOrDefault(t => t.TaskId == id) : null;

    private string Session => SessionId.ToString();

    protected override async Task OnInitializedAsync()
    {
        var authState = await AuthState.GetAuthenticationStateAsync();
        if (authState.User.Identity?.IsAuthenticated != true)
        {
            Login();
            return;
        }

        // The participant identity in the room is keyed by the 'oid' claim (see PlanningRoomHub),
        // so match on it rather than the display name, which can collide between users.
        _myParticipantId = authState.User.FindFirst("oid")?.Value;
        _isGuest = authState.User.FindFirst("is_guest")?.Value == "true";

        try
        {
            var session = await SessionService.GetSessionAsync(SessionId);
            _projectId = session.ProjectId;
            if (session.Status != SessionStatusDto.Active)
            {
                _errorKey = "Voting_NotAvailable";
                _loading = false;
                return;
            }
        }
        catch (RpcException)
        {
            _errorKey = "Voting_LoadError";
            _loading = false;
            return;
        }

        RoomService.RoomStateChanged += OnRoomStateChanged;

        try
        {
            await RoomService.ConnectAsync();
            await RoomService.JoinRoomAsync(Session);
        }
        catch (HubException)
        {
            _errorKey = "Voting_NotAvailable";
        }
        catch (Exception)
        {
            _errorKey = "Voting_ConnectionError";
        }

        _loading = false;
    }

    private void OnRoomStateChanged(PlanningRoomState state)
    {
        if (_activeTaskId != state.CurrentTaskId)
        {
            _activeTaskId = state.CurrentTaskId;
            _myVote = null;
        }

        var me = state.Participants.FirstOrDefault(p =>
            string.Equals(p.ParticipantId, _myParticipantId, StringComparison.OrdinalIgnoreCase));
        if (me is not null && !me.HasVoted)
        {
            _myVote = null;
        }

        _state = state;
        InvokeAsync(StateHasChanged);
    }

    private void Login() =>
        Navigation.NavigateTo(
            $"/auth/login?returnUrl={Uri.EscapeDataString(Navigation.Uri)}",
            forceLoad: true);

    private void BackToProjectSessions()
    {
        if (_projectId == Guid.Empty)
        {
            Navigation.NavigateTo("/projects");
            return;
        }

        Navigation.NavigateTo($"/projects/{_projectId:D}/sessions");
    }

    private async Task CastVoteAsync(string value)
    {
        _myVote = value;
        await RunAsync(() => RoomService.CastVoteAsync(Session, value));
    }

    private Task SetActiveTaskAsync(Guid taskId) =>
        _isGuest ? Task.CompletedTask : RunAsync(() => RoomService.SetActiveTaskAsync(Session, taskId.ToString()));

    private Task RevealVotesAsync() =>
        _isGuest ? Task.CompletedTask : RunAsync(() => RoomService.RevealVotesAsync(Session));

    private Task ResetRoundAsync() =>
        _isGuest ? Task.CompletedTask : RunAsync(() => RoomService.ResetRoundAsync(Session));

    private Task SelectEstimateAsync(string value)
    {
        if (_isGuest || _activeTaskId is not { } taskId)
        {
            return Task.CompletedTask;
        }

        return RunAsync(() => RoomService.SelectEstimateAsync(Session, taskId.ToString(), value));
    }

    private async Task RunAsync(Func<Task> action)
    {
        _busy = true;
        try
        {
            await action();
        }
        catch (Exception)
        {
            Snackbar.Add(L["Voting_ActionError"], Severity.Error);
        }
        finally
        {
            _busy = false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        RoomService.RoomStateChanged -= OnRoomStateChanged;
        try
        {
            await RoomService.LeaveRoomAsync(Session);
        }
        catch
        {
            // ignored — connection may already be gone on teardown
        }
    }
}
