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
    private string? _errorKey;
    private string? _myDisplayName;
    private string? _myVote;
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

        _myDisplayName = authState.User.Identity?.Name;

        try
        {
            var session = await SessionService.GetSessionAsync(SessionId);
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
            string.Equals(p.DisplayName, _myDisplayName, StringComparison.OrdinalIgnoreCase));
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

    private async Task CastVoteAsync(string value)
    {
        _myVote = value;
        await RunAsync(() => RoomService.CastVoteAsync(Session, value));
    }

    private Task SetActiveTaskAsync(Guid taskId) =>
        RunAsync(() => RoomService.SetActiveTaskAsync(Session, taskId.ToString()));

    private Task RevealVotesAsync() =>
        RunAsync(() => RoomService.RevealVotesAsync(Session));

    private Task ResetRoundAsync() =>
        RunAsync(() => RoomService.ResetRoundAsync(Session));

    private Task SelectEstimateAsync(string value)
    {
        if (_activeTaskId is not { } taskId)
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
