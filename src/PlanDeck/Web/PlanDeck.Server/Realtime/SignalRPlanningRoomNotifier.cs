using Microsoft.AspNetCore.SignalR;
using PlanDeck.Application.Abstractions;
using PlanDeck.Application.Planning;
using PlanDeck.Server.Hubs;

namespace PlanDeck.Server.Realtime;

/// <summary>
/// SignalR-backed <see cref="IPlanningRoomNotifier"/>: reconciles the in-memory
/// voting room with persisted task changes and broadcasts the new state to the
/// session's SignalR group so live participants see add/edit/remove in real time.
/// </summary>
public sealed class SignalRPlanningRoomNotifier(
    IPlanningRoomService planningRoomService,
    ICurrentUserContext currentUserContext,
    IHubContext<PlanningRoomHub> hubContext) : IPlanningRoomNotifier
{
    public async Task NotifyTasksChangedAsync(
        Guid sessionId,
        IReadOnlyList<PlanningRoomTaskSnapshot> tasks,
        CancellationToken cancellationToken)
    {
        var key = new RoomKey(currentUserContext.TenantId, sessionId);
        var state = planningRoomService.SyncTasks(key, tasks);
        await hubContext.Clients.Group(key.GroupName).SendAsync("RoomStateChanged", state, cancellationToken);
    }
}
