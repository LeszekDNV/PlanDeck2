using Microsoft.AspNetCore.SignalR;
using PlanDeck.Application.Planning;

namespace PlanDeck.Server.Hubs;

public sealed class PlanningRoomHub(IPlanningRoomService planningRoomService) : Hub
{
    public async Task JoinRoom(string sessionId, string participantId, string displayName)
    {
        var key = BuildKey(sessionId);
        await Groups.AddToGroupAsync(Context.ConnectionId, key.GroupName, Context.ConnectionAborted);
        var state = planningRoomService.Join(key, participantId, displayName, Context.ConnectionId);
        await Clients.Group(key.GroupName).SendAsync("RoomStateChanged", state, Context.ConnectionAborted);
    }

    public async Task LeaveRoom(string sessionId, string participantId)
    {
        var key = BuildKey(sessionId);
        var state = planningRoomService.Leave(key, participantId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, key.GroupName, Context.ConnectionAborted);
        await Clients.Group(key.GroupName).SendAsync("RoomStateChanged", state, Context.ConnectionAborted);
    }

    public async Task CastVote(string sessionId, string participantId, string vote)
    {
        var key = BuildKey(sessionId);
        var state = planningRoomService.CastVote(key, participantId, vote);
        await Clients.Group(key.GroupName).SendAsync("RoomStateChanged", state, Context.ConnectionAborted);
    }

    public async Task RevealVotes(string sessionId)
    {
        var key = BuildKey(sessionId);
        var state = planningRoomService.RevealVotes(key);
        await Clients.Group(key.GroupName).SendAsync("RoomStateChanged", state, Context.ConnectionAborted);
    }

    public async Task ResetRound(string sessionId)
    {
        var key = BuildKey(sessionId);
        var state = planningRoomService.ResetRound(key);
        await Clients.Group(key.GroupName).SendAsync("RoomStateChanged", state, Context.ConnectionAborted);
    }

    private static RoomKey BuildKey(string sessionId)
    {
        if (!Guid.TryParse(sessionId, out var sessionGuid))
        {
            throw new HubException("A valid session id is required.");
        }

        return new RoomKey(Guid.Empty, sessionGuid);
    }
}
