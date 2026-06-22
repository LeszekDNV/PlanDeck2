using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using PlanDeck.Application.Planning;
using PlanDeck.Server.Identity;

namespace PlanDeck.Server.Hubs;

[Authorize]
public sealed class PlanningRoomHub(
    IPlanningRoomService planningRoomService,
    IVotingRoundService votingRoundService,
    RequestPrincipalAccessor principalAccessor) : Hub
{
    public async Task JoinRoom(string sessionId)
    {
        var key = await AuthorizeAsync(sessionId);

        var seed = await votingRoundService.LoadRoomSeedAsync(key.SessionId, Context.ConnectionAborted);
        if (seed is not null)
        {
            planningRoomService.EnsureSeeded(key, seed.Tasks, seed.ScaleValues);
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, key.GroupName, Context.ConnectionAborted);
        var state = planningRoomService.Join(key, ParticipantId, DisplayName, Context.ConnectionId);
        await Clients.Group(key.GroupName).SendAsync("RoomStateChanged", state, Context.ConnectionAborted);
    }

    public async Task LeaveRoom(string sessionId)
    {
        var key = BuildKey(sessionId);
        var state = planningRoomService.Leave(key, ParticipantId, Context.ConnectionId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, key.GroupName, Context.ConnectionAborted);
        await Clients.Group(key.GroupName).SendAsync("RoomStateChanged", state, Context.ConnectionAborted);
    }

    public async Task CastVote(string sessionId, string vote)
    {
        var key = await AuthorizeAsync(sessionId);
        var state = planningRoomService.CastVote(key, ParticipantId, vote);
        await Clients.Group(key.GroupName).SendAsync("RoomStateChanged", state, Context.ConnectionAborted);
    }

    public async Task RevealVotes(string sessionId)
    {
        var key = await AuthorizeAsync(sessionId);
        var state = planningRoomService.RevealVotes(key);
        await Clients.Group(key.GroupName).SendAsync("RoomStateChanged", state, Context.ConnectionAborted);
    }

    public async Task ResetRound(string sessionId)
    {
        var key = await AuthorizeAsync(sessionId);
        var state = planningRoomService.ResetRound(key);
        await Clients.Group(key.GroupName).SendAsync("RoomStateChanged", state, Context.ConnectionAborted);
    }

    public async Task SetActiveTask(string sessionId, string taskId)
    {
        var key = await AuthorizeAsync(sessionId);
        var state = planningRoomService.SetActiveTask(key, ParseTaskId(taskId));
        await Clients.Group(key.GroupName).SendAsync("RoomStateChanged", state, Context.ConnectionAborted);
    }

    public async Task SelectEstimate(string sessionId, string taskId, string value)
    {
        var key = await AuthorizeAsync(sessionId);
        var taskGuid = ParseTaskId(taskId);

        var persisted = await votingRoundService.SelectEstimateAsync(
            key.SessionId, taskGuid, value, Context.ConnectionAborted);
        if (!persisted)
        {
            throw new HubException("The agreed estimate could not be saved.");
        }

        var state = planningRoomService.ApplyAgreedEstimate(key, taskGuid, value);
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

    private async Task<RoomKey> AuthorizeAsync(string sessionId)
    {
        // Establish the caller for tenant-scoped data access within this hub invocation's DI scope.
        principalAccessor.Principal = Context.User;

        var key = BuildKey(sessionId);
        var email = Email
            ?? throw new HubException("Authenticated 'email' claim is required.");

        var isMember = await votingRoundService.IsAssignedMemberAsync(
            key.SessionId, email, Context.ConnectionAborted);
        if (!isMember)
        {
            throw new HubException("You are not an assigned member of this session.");
        }

        return key;
    }

    private string ParticipantId => ReadRequiredClaim("oid");

    private string DisplayName
    {
        get
        {
            var user = Context.User;
            var name = user?.FindFirstValue("name") ?? Email;
            return string.IsNullOrWhiteSpace(name) ? "Guest" : name;
        }
    }

    // Mirrors HttpContextCurrentUserContext.Email: the real OIDC scheme runs MapInboundClaims=false
    // and may emit only preferred_username, so never read the email claim alone.
    private string? Email
    {
        get
        {
            var user = Context.User;
            return user?.FindFirstValue("email") ?? user?.FindFirstValue("preferred_username");
        }
    }

    private static Guid ParseTaskId(string taskId)
    {
        if (!Guid.TryParse(taskId, out var taskGuid))
        {
            throw new HubException("A valid task id is required.");
        }

        return taskGuid;
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
