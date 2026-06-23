using PlanDeck.Application.Abstractions;

namespace PlanDeck.Application.Services;

/// <summary>
/// Default no-op notifier used outside the SignalR-hosted server (tests and any
/// non-hosted path). The Web host registers the real implementation over this.
/// </summary>
public sealed class NoOpPlanningRoomNotifier : IPlanningRoomNotifier
{
    public Task NotifyTasksChangedAsync(
        Guid sessionId,
        IReadOnlyList<PlanningRoomTaskSnapshot> tasks,
        CancellationToken cancellationToken)
        => Task.CompletedTask;
}
