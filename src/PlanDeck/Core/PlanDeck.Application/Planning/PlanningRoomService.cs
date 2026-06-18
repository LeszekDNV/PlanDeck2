using System.Collections.Concurrent;
using PlanDeck.Core.Shared.Realtime;

namespace PlanDeck.Application.Planning;

public sealed class PlanningRoomService : IPlanningRoomService
{
    private readonly ConcurrentDictionary<string, PlanningRoom> _rooms = new(StringComparer.OrdinalIgnoreCase);

    public PlanningRoomState Join(string sessionId, string participantId, string displayName)
    {
        var room = GetRoom(sessionId);
        lock (room)
        {
            room.Participants[participantId] = new Participant(displayName);
            return ToState(sessionId, room);
        }
    }

    public PlanningRoomState Leave(string sessionId, string participantId)
    {
        var room = GetRoom(sessionId);
        lock (room)
        {
            room.Participants.Remove(participantId);
            return ToState(sessionId, room);
        }
    }

    public PlanningRoomState CastVote(string sessionId, string participantId, string vote)
    {
        var room = GetRoom(sessionId);
        lock (room)
        {
            if (!room.Participants.TryGetValue(participantId, out var participant))
            {
                throw new InvalidOperationException("Participant must join the planning room before voting.");
            }

            participant.Vote = vote;
            return ToState(sessionId, room);
        }
    }

    public PlanningRoomState RevealVotes(string sessionId)
    {
        var room = GetRoom(sessionId);
        lock (room)
        {
            room.IsRevealed = true;
            return ToState(sessionId, room);
        }
    }

    public PlanningRoomState ResetRound(string sessionId)
    {
        var room = GetRoom(sessionId);
        lock (room)
        {
            room.IsRevealed = false;
            foreach (var participant in room.Participants.Values)
            {
                participant.Vote = null;
            }

            return ToState(sessionId, room);
        }
    }

    private PlanningRoom GetRoom(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ArgumentException("Session ID is required.", nameof(sessionId));
        }

        return _rooms.GetOrAdd(sessionId, _ => new PlanningRoom());
    }

    private static PlanningRoomState ToState(string sessionId, PlanningRoom room)
    {
        return new PlanningRoomState(
            sessionId,
            room.IsRevealed,
            room.Participants
                .OrderBy(participant => participant.Value.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Select(participant => new PlanningParticipantState(
                    participant.Key,
                    participant.Value.DisplayName,
                    participant.Value.Vote is not null,
                    room.IsRevealed ? participant.Value.Vote : null))
                .ToArray());
    }

    private sealed class PlanningRoom
    {
        public bool IsRevealed { get; set; }

        public Dictionary<string, Participant> Participants { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class Participant(string displayName)
    {
        public string DisplayName { get; } = string.IsNullOrWhiteSpace(displayName) ? "Guest" : displayName;

        public string? Vote { get; set; }
    }
}
