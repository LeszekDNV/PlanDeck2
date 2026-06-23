using PlanDeck.Core.Shared.Realtime;

namespace PlanDeck.Client.Services;

public interface IPlanningRoomClientService : IAsyncDisposable
{
    event Action<PlanningRoomState>? RoomStateChanged;

    Task ConnectAsync();

    Task JoinRoomAsync(string sessionId);

    Task LeaveRoomAsync(string sessionId);

    Task CastVoteAsync(string sessionId, string vote);

    Task RevealVotesAsync(string sessionId);

    Task ResetRoundAsync(string sessionId);

    Task SetActiveTaskAsync(string sessionId, string taskId);

    Task SelectEstimateAsync(string sessionId, string taskId, string value);
}
