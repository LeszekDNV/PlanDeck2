namespace PlanDeck.Application.Abstractions;

/// <summary>
/// Immutable snapshot of a session task used to reconcile the live voting room
/// after a task mutation on an Active session.
/// </summary>
public readonly record struct PlanningRoomTaskSnapshot(
    Guid TaskId,
    string Title,
    string? Description,
    int SortOrder,
    string? AgreedEstimate);

/// <summary>
/// Notifies the live voting room that an Active session's tasks changed so
/// connected participants see add/edit/remove operations in real time. The
/// Application layer depends only on this abstraction; the SignalR-backed
/// implementation lives in the Web host.
/// </summary>
public interface IPlanningRoomNotifier
{
    Task NotifyTasksChangedAsync(
        Guid sessionId,
        IReadOnlyList<PlanningRoomTaskSnapshot> tasks,
        CancellationToken cancellationToken);
}
