using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using PlanDeck.Core.Shared.Realtime;

namespace PlanDeck.Client.Services;

public sealed class PlanningRoomClientService(NavigationManager navigationManager) : IPlanningRoomClientService
{
    private readonly HubConnection _hubConnection = new HubConnectionBuilder()
        .WithUrl(navigationManager.ToAbsoluteUri("/hubs/planning-room"))
        .WithAutomaticReconnect()
        .Build();

    private bool _handlerRegistered;
    private string? _joinedSessionId;

    public event Action<PlanningRoomState>? RoomStateChanged;

    public async Task ConnectAsync()
    {
        if (!_handlerRegistered)
        {
            _hubConnection.On<PlanningRoomState>("RoomStateChanged", state => RoomStateChanged?.Invoke(state));
            _hubConnection.Reconnected += async _ =>
            {
                if (_joinedSessionId is { } sessionId)
                {
                    await _hubConnection.InvokeAsync("JoinRoom", sessionId);
                }
            };
            _handlerRegistered = true;
        }

        if (_hubConnection.State == HubConnectionState.Disconnected)
        {
            await _hubConnection.StartAsync();
        }
    }

    public async Task JoinRoomAsync(string sessionId)
    {
        _joinedSessionId = sessionId;
        await _hubConnection.InvokeAsync("JoinRoom", sessionId);
    }

    public async Task LeaveRoomAsync(string sessionId)
    {
        await _hubConnection.InvokeAsync("LeaveRoom", sessionId);
        if (string.Equals(_joinedSessionId, sessionId, StringComparison.Ordinal))
        {
            _joinedSessionId = null;
        }
    }

    public Task CastVoteAsync(string sessionId, string vote)
    {
        return _hubConnection.InvokeAsync("CastVote", sessionId, vote);
    }

    public Task RevealVotesAsync(string sessionId)
    {
        return _hubConnection.InvokeAsync("RevealVotes", sessionId);
    }

    public Task ResetRoundAsync(string sessionId)
    {
        return _hubConnection.InvokeAsync("ResetRound", sessionId);
    }

    public Task SetActiveTaskAsync(string sessionId, string taskId)
    {
        return _hubConnection.InvokeAsync("SetActiveTask", sessionId, taskId);
    }

    public Task SelectEstimateAsync(string sessionId, string taskId, string value)
    {
        return _hubConnection.InvokeAsync("SelectEstimate", sessionId, taskId, value);
    }

    public async ValueTask DisposeAsync()
    {
        await _hubConnection.DisposeAsync();
    }
}
