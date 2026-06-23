using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
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
    IHubContext<PlanningRoomHub> hubContext,
    ILogger<SignalRPlanningRoomNotifier> logger) : IPlanningRoomNotifier
{
    public async Task NotifyTasksChangedAsync(
        Guid sessionId,
        IReadOnlyList<PlanningRoomTaskSnapshot> tasks,
        CancellationToken cancellationToken)
    {
        // Best-effort live notification: the task mutation has already been
        // persisted, so a failed reconcile/broadcast must never bubble up and
        // fail the committed gRPC call (which would trigger a duplicating retry).
        try
        {
            var key = new RoomKey(currentUserContext.TenantId, sessionId);
            var state = planningRoomService.SyncTasks(key, tasks);
            await hubContext.Clients.Group(key.GroupName).SendAsync("RoomStateChanged", state, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to broadcast task changes for session {SessionId}.", sessionId);
        }
    }
}
