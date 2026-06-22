using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using PlanDeck.Application.Planning;

namespace PlanDeck.Server.Hubs;

[Authorize]
public sealed class PlanningRoomHub(IPlanningRoomService planningRoomService) : Hub
{
    public async Task JoinRoom(string sessionId)
    {
        var key = BuildKey(sessionId);
        await Groups.AddToGroupAsync(Context.ConnectionId, key.GroupName, Context.ConnectionAborted);
        var state = planningRoomService.Join(key, ParticipantId, DisplayName, Context.ConnectionId);
        await Clients.Group(key.GroupName).SendAsync("RoomStateChanged", state, Context.ConnectionAborted);
    }

    public async Task LeaveRoom(string sessionId)
    {
        var key = BuildKey(sessionId);
        var state = planningRoomService.Leave(key, ParticipantId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, key.GroupName, Context.ConnectionAborted);
        await Clients.Group(key.GroupName).SendAsync("RoomStateChanged", state, Context.ConnectionAborted);
    }

    public async Task CastVote(string sessionId, string vote)
    {
        var key = BuildKey(sessionId);
        var state = planningRoomService.CastVote(key, ParticipantId, vote);
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

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var result = planningRoomService.Disconnect(Context.ConnectionId);
        if (result is { } resolved)
        {
            await Clients.Group(resolved.Key.GroupName).SendAsync("RoomStateChanged", resolved.State);
        }

        await base.OnDisconnectedAsync(exception);
    }

    private string ParticipantId => ReadRequiredClaim("oid");

    private string DisplayName
    {
        get
        {
            var user = Context.User;
            var name = user?.FindFirstValue("name") ?? user?.FindFirstValue("email");
            return string.IsNullOrWhiteSpace(name) ? "Guest" : name;
        }
    }

    private RoomKey BuildKey(string sessionId)
    {
        if (!Guid.TryParse(sessionId, out var sessionGuid))
        {
            throw new HubException("A valid session id is required.");
        }

        if (!Guid.TryParse(ReadRequiredClaim("tid"), out var tenantGuid))
        {
            throw new HubException("Authenticated tenant claim is missing or invalid.");
        }

        return new RoomKey(tenantGuid, sessionGuid);
    }

    private string ReadRequiredClaim(string claimType)
    {
        var value = Context.User?.FindFirstValue(claimType);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new HubException($"Authenticated '{claimType}' claim is required.");
        }

        return value;
    }
}
