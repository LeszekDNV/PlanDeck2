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

    public event Action<PlanningRoomState>? RoomStateChanged;

    public async Task ConnectAsync()
    {
        _hubConnection.On<PlanningRoomState>("RoomStateChanged", state => RoomStateChanged?.Invoke(state));

        if (_hubConnection.State == HubConnectionState.Disconnected)
        {
            await _hubConnection.StartAsync();
        }
    }

    public Task JoinRoomAsync(string sessionId, string participantId, string displayName)
    {
        return _hubConnection.InvokeAsync("JoinRoom", sessionId, participantId, displayName);
    }

    public Task LeaveRoomAsync(string sessionId, string participantId)
    {
        return _hubConnection.InvokeAsync("LeaveRoom", sessionId, participantId);
    }

    public Task CastVoteAsync(string sessionId, string participantId, string vote)
    {
        return _hubConnection.InvokeAsync("CastVote", sessionId, participantId, vote);
    }

    public Task RevealVotesAsync(string sessionId)
    {
        return _hubConnection.InvokeAsync("RevealVotes", sessionId);
    }

    public Task ResetRoundAsync(string sessionId)
    {
        return _hubConnection.InvokeAsync("ResetRound", sessionId);
    }

    public async ValueTask DisposeAsync()
    {
        await _hubConnection.DisposeAsync();
    }
}
