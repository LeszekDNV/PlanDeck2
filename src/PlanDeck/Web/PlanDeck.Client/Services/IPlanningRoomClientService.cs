using PlanDeck.Core.Shared.Realtime;

namespace PlanDeck.Client.Services;

public interface IPlanningRoomClientService : IAsyncDisposable
{
    event Action<PlanningRoomState>? RoomStateChanged;

    Task ConnectAsync();

    Task JoinRoomAsync(string sessionId, string participantId, string displayName);

    Task LeaveRoomAsync(string sessionId, string participantId);

    Task CastVoteAsync(string sessionId, string participantId, string vote);

    Task RevealVotesAsync(string sessionId);

    Task ResetRoundAsync(string sessionId);
}
