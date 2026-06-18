using Microsoft.AspNetCore.SignalR;
using PlanDeck.Application.Planning;

namespace PlanDeck.Server.Hubs;

public sealed class PlanningRoomHub(IPlanningRoomService planningRoomService) : Hub
{
    public async Task JoinRoom(string sessionId, string participantId, string displayName)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, sessionId, Context.ConnectionAborted);
        var state = planningRoomService.Join(sessionId, participantId, displayName);
        await Clients.Group(sessionId).SendAsync("RoomStateChanged", state, Context.ConnectionAborted);
    }

    public async Task LeaveRoom(string sessionId, string participantId)
    {
        var state = planningRoomService.Leave(sessionId, participantId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, sessionId, Context.ConnectionAborted);
        await Clients.Group(sessionId).SendAsync("RoomStateChanged", state, Context.ConnectionAborted);
    }

    public async Task CastVote(string sessionId, string participantId, string vote)
    {
        var state = planningRoomService.CastVote(sessionId, participantId, vote);
        await Clients.Group(sessionId).SendAsync("RoomStateChanged", state, Context.ConnectionAborted);
    }

    public async Task RevealVotes(string sessionId)
    {
        var state = planningRoomService.RevealVotes(sessionId);
        await Clients.Group(sessionId).SendAsync("RoomStateChanged", state, Context.ConnectionAborted);
    }

    public async Task ResetRound(string sessionId)
    {
        var state = planningRoomService.ResetRound(sessionId);
        await Clients.Group(sessionId).SendAsync("RoomStateChanged", state, Context.ConnectionAborted);
    }
}
