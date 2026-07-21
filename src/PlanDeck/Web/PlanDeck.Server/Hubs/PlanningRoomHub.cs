using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using PlanDeck.Application.Planning;
using PlanDeck.Server.Identity;

namespace PlanDeck.Server.Hubs;

[Authorize(Policy = GuestAuthentication.RoomParticipantPolicy)]
public sealed class PlanningRoomHub(
    IPlanningRoomService planningRoomService,
    IVotingRoundService votingRoundService,
    RequestPrincipalAccessor principalAccessor) : Hub
{
    public async Task JoinRoom(string sessionId)
    {
        // Establish the caller for tenant-scoped data access within this hub invocation's DI scope.
        principalAccessor.Principal = Context.User;
        var key = BuildKey(sessionId);

        // Guests are admitted by their validated guest cookie + sid scope (already enforced by
        // BuildKey); members go through the assigned-member/creator authorization. Both need a fresh
        // Active session seed.
        var seed = IsGuest
            ? await votingRoundService.LoadActiveSessionSeedAsync(key.SessionId, Context.ConnectionAborted)
            : await votingRoundService.AuthorizeAndLoadSeedAsync(
                key.SessionId, UserId, Email, Context.ConnectionAborted);
        if (seed is null)
        {
            throw new HubException(IsGuest
                ? "This session is not open for guests."
                : "You are not an assigned member of this session.");
        }

        planningRoomService.EnsureSeeded(key, seed.Tasks, seed.ScaleValues);

        var state = planningRoomService.Join(key, ParticipantId, DisplayName, Context.ConnectionId);
        try
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, key.GroupName, Context.ConnectionAborted);
        }
        catch
        {
            planningRoomService.Disconnect(Context.ConnectionId);
            throw;
        }

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
        EnsureNotGuest();
        var key = await AuthorizeAsync(sessionId);
        var state = planningRoomService.RevealVotes(key);
        await Clients.Group(key.GroupName).SendAsync("RoomStateChanged", state, Context.ConnectionAborted);
    }

    public async Task ResetRound(string sessionId)
    {
        EnsureNotGuest();
        var key = await AuthorizeAsync(sessionId);

        // Persist the agreed-estimate clear BEFORE mutating in-memory state so a DB failure
        // cannot leave memory and the database out of sync (mirrors SelectEstimate's persist-first order).
        if (planningRoomService.GetState(key).CurrentTaskId is { } taskId)
        {
            var persisted = await votingRoundService.SelectEstimateAsync(
                key.SessionId, taskId, null, Context.ConnectionAborted);
            if (!persisted)
            {
                throw new HubException("The agreed estimate could not be cleared.");
            }
        }

        var state = planningRoomService.ResetRound(key);
        await Clients.Group(key.GroupName).SendAsync("RoomStateChanged", state, Context.ConnectionAborted);
    }

    public async Task SetActiveTask(string sessionId, string taskId)
    {
        EnsureNotGuest();
        var key = await AuthorizeAsync(sessionId);
        var state = planningRoomService.SetActiveTask(key, ParseTaskId(taskId));
        await Clients.Group(key.GroupName).SendAsync("RoomStateChanged", state, Context.ConnectionAborted);
    }

    public async Task SelectEstimate(string sessionId, string taskId, string value)
    {
        EnsureNotGuest();
        var key = await AuthorizeAsync(sessionId);
        var taskGuid = ParseTaskId(taskId);

        if (!planningRoomService.IsValidEstimate(key, value))
        {
            throw new HubException("The agreed estimate is not a valid value for the session scale.");
        }

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

        // Guests are confined to their own session by BuildKey's sid scope check and authorized by
        // the validated guest cookie — they never go through the member/creator lookup.
        if (IsGuest)
        {
            return key;
        }

        var email = Email;

        var isAuthorized = await votingRoundService.IsAuthorizedParticipantAsync(
            key.SessionId, UserId, email, Context.ConnectionAborted);
        if (!isAuthorized)
        {
            throw new HubException("You are not an assigned member of this session.");
        }

        return key;
    }

    private string ParticipantId => ReadRequiredClaim("oid");

    private Guid UserId
    {
        get
        {
            if (!Guid.TryParse(ReadRequiredClaim("oid"), out var userId))
            {
                throw new HubException("Authenticated 'oid' claim is missing or invalid.");
            }

            return userId;
        }
    }

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

        EnsureSessionScope(sessionGuid);

        return new RoomKey(tenantGuid, sessionGuid);
    }

    private bool IsGuest =>
        string.Equals(
            Context.User?.FindFirstValue(GuestAuthentication.IsGuestClaim),
            "true",
            StringComparison.OrdinalIgnoreCase);

    // A guest cookie is bound to a single session via its 'sid' claim. Reject any attempt to act on
    // a different session, so a guest can never reach sibling sessions in the same tenant.
    private void EnsureSessionScope(Guid sessionId)
    {
        if (!IsGuest)
        {
            return;
        }

        var scoped = Context.User?.FindFirstValue(GuestAuthentication.SessionIdClaim);
        if (!Guid.TryParse(scoped, out var scopedSessionId) || scopedSessionId != sessionId)
        {
            throw new HubException("This guest link is not valid for the requested session.");
        }
    }

    private void EnsureNotGuest()
    {
        if (IsGuest)
        {
            throw new HubException("Guests can only vote; this action is restricted to members.");
        }
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
