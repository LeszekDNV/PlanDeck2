using System.Collections.Concurrent;
using PlanDeck.Core.Shared.Realtime;

namespace PlanDeck.Application.Planning;

public sealed class PlanningRoomService : IPlanningRoomService
{
    private readonly ConcurrentDictionary<RoomKey, PlanningRoom> _rooms = new();
    private readonly ConcurrentDictionary<string, ConnectionOwner> _connections = new(StringComparer.Ordinal);

    public PlanningRoomState Join(RoomKey key, string participantId, string displayName, string connectionId)
    {
        if (string.IsNullOrWhiteSpace(participantId))
        {
            throw new ArgumentException("Participant ID is required.", nameof(participantId));
        }

        if (string.IsNullOrWhiteSpace(connectionId))
        {
            throw new ArgumentException("Connection ID is required.", nameof(connectionId));
        }

        var room = GetRoom(key);
        lock (room)
        {
            if (!room.Participants.TryGetValue(participantId, out var participant))
            {
                participant = new Participant(displayName);
                room.Participants[participantId] = participant;
            }

            participant.Connections.Add(connectionId);
            _connections[connectionId] = new ConnectionOwner(key, participantId);
            return ToState(key, room);
        }
    }

    public PlanningRoomState Leave(RoomKey key, string participantId, string connectionId)
    {
        var room = GetRoom(key);
        lock (room)
        {
            _connections.TryRemove(connectionId, out _);

            if (room.Participants.TryGetValue(participantId, out var participant))
            {
                participant.Connections.Remove(connectionId);
                if (participant.Connections.Count == 0)
                {
                    room.Participants.Remove(participantId);
                }
            }

            return ToState(key, room);
        }
    }

    public (RoomKey Key, PlanningRoomState State)? Disconnect(string connectionId)
    {
        if (string.IsNullOrWhiteSpace(connectionId) || !_connections.TryRemove(connectionId, out var owner))
        {
            return null;
        }

        if (!_rooms.TryGetValue(owner.Key, out var room))
        {
            return null;
        }

        lock (room)
        {
            if (room.Participants.TryGetValue(owner.ParticipantId, out var participant))
            {
                participant.Connections.Remove(connectionId);
            }

            return (owner.Key, ToState(owner.Key, room));
        }
    }

    public PlanningRoomState CastVote(RoomKey key, string participantId, string vote)
    {
        var room = GetRoom(key);
        lock (room)
        {
            if (!room.Participants.TryGetValue(participantId, out var participant))
            {
                throw new InvalidOperationException("Participant must join the planning room before voting.");
            }

            if (room.IsRevealed)
            {
                throw new InvalidOperationException("Votes cannot be cast after the round has been revealed.");
            }

            participant.Vote = vote;
            return ToState(key, room);
        }
    }

    public PlanningRoomState RevealVotes(RoomKey key)
    {
        var room = GetRoom(key);
        lock (room)
        {
            room.IsRevealed = true;
            return ToState(key, room);
        }
    }

    public PlanningRoomState ResetRound(RoomKey key)
    {
        var room = GetRoom(key);
        lock (room)
        {
            room.IsRevealed = false;
            foreach (var participant in room.Participants.Values)
            {
                participant.Vote = null;
            }

            return ToState(key, room);
        }
    }

    public PlanningRoomState GetState(RoomKey key)
    {
        var room = GetRoom(key);
        lock (room)
        {
            return ToState(key, room);
        }
    }

    private PlanningRoom GetRoom(RoomKey key)
    {
        return _rooms.GetOrAdd(key, _ => new PlanningRoom());
    }

    private static PlanningRoomState ToState(RoomKey key, PlanningRoom room)
    {
        return new PlanningRoomState(
            key.SessionId.ToString(),
            room.IsRevealed,
            room.Participants
                .OrderBy(participant => participant.Value.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Select(participant => new PlanningParticipantState(
                    participant.Key,
                    participant.Value.DisplayName,
                    participant.Value.Vote is not null,
                    room.IsRevealed ? participant.Value.Vote : null,
                    participant.Value.Connections.Count > 0))
                .ToArray());
    }

    private readonly record struct ConnectionOwner(RoomKey Key, string ParticipantId);

    private sealed class PlanningRoom
    {
        public bool IsRevealed { get; set; }

        public Dictionary<string, Participant> Participants { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class Participant(string displayName)
    {
        public string DisplayName { get; } = string.IsNullOrWhiteSpace(displayName) ? "Guest" : displayName;

        public string? Vote { get; set; }

        public HashSet<string> Connections { get; } = new(StringComparer.Ordinal);
    }
}
