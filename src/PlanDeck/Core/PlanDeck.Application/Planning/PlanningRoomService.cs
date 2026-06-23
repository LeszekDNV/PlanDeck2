using System.Collections.Concurrent;
using PlanDeck.Core.Shared.Realtime;

namespace PlanDeck.Application.Planning;

public sealed class PlanningRoomService : IPlanningRoomService
{
    private readonly ConcurrentDictionary<RoomKey, PlanningRoom> _rooms = new();
    private readonly ConcurrentDictionary<string, ConnectionOwner> _connections = new(StringComparer.Ordinal);

    public PlanningRoomState EnsureSeeded(
        RoomKey key,
        IReadOnlyList<(Guid TaskId, string Title, int SortOrder, string? AgreedEstimate)> tasks,
        IReadOnlyList<string> scaleValues)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        ArgumentNullException.ThrowIfNull(scaleValues);

        var room = GetRoom(key);
        lock (room)
        {
            if (!room.Seeded)
            {
                room.ScaleValues = [.. scaleValues];
                foreach (var task in tasks.OrderBy(t => t.SortOrder))
                {
                    room.Tasks.Add(new RoomTask(task.TaskId, task.Title, task.SortOrder)
                    {
                        AgreedEstimate = task.AgreedEstimate
                    });
                }

                room.CurrentTaskId = room.Tasks.OrderBy(t => t.SortOrder).FirstOrDefault()?.TaskId;
                room.Seeded = true;
            }

            return ToState(key, room);
        }
    }

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
                    RemoveVotes(room, participantId);
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
            if (!room.Participants.TryGetValue(participantId, out _))
            {
                throw new InvalidOperationException("Participant must join the planning room before voting.");
            }

            var task = ActiveTask(room)
                ?? throw new InvalidOperationException("There is no active task to vote on.");

            if (task.IsRevealed)
            {
                throw new InvalidOperationException("Votes cannot be cast after the round has been revealed.");
            }

            if (!room.ScaleValues.Contains(vote, StringComparer.Ordinal))
            {
                throw new InvalidOperationException("Vote is not a valid value for the session scale.");
            }

            task.Votes[participantId] = vote;
            return ToState(key, room);
        }
    }

    public PlanningRoomState RevealVotes(RoomKey key)
    {
        var room = GetRoom(key);
        lock (room)
        {
            var task = ActiveTask(room)
                ?? throw new InvalidOperationException("There is no active task to reveal.");

            task.IsRevealed = true;
            return ToState(key, room);
        }
    }

    public PlanningRoomState ResetRound(RoomKey key)
    {
        var room = GetRoom(key);
        lock (room)
        {
            var task = ActiveTask(room)
                ?? throw new InvalidOperationException("There is no active task to reset.");

            task.IsRevealed = false;
            task.Votes.Clear();
            task.AgreedEstimate = null;
            return ToState(key, room);
        }
    }

    public PlanningRoomState SetActiveTask(RoomKey key, Guid taskId)
    {
        var room = GetRoom(key);
        lock (room)
        {
            if (room.Tasks.All(t => t.TaskId != taskId))
            {
                throw new InvalidOperationException("Task does not belong to this planning room.");
            }

            room.CurrentTaskId = taskId;
            return ToState(key, room);
        }
    }

    public PlanningRoomState ApplyAgreedEstimate(RoomKey key, Guid taskId, string? estimate)
    {
        var room = GetRoom(key);
        lock (room)
        {
            var task = room.Tasks.FirstOrDefault(t => t.TaskId == taskId)
                ?? throw new InvalidOperationException("Task does not belong to this planning room.");

            task.AgreedEstimate = estimate;
            return ToState(key, room);
        }
    }

    public bool IsValidEstimate(RoomKey key, string? estimate)
    {
        // A null estimate clears the agreed value (e.g. on reset) and is always allowed.
        if (estimate is null)
        {
            return true;
        }

        var room = GetRoom(key);
        lock (room)
        {
            return room.ScaleValues.Contains(estimate, StringComparer.Ordinal);
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

    private static RoomTask? ActiveTask(PlanningRoom room)
    {
        return room.CurrentTaskId is Guid id
            ? room.Tasks.FirstOrDefault(t => t.TaskId == id)
            : null;
    }

    private static void RemoveVotes(PlanningRoom room, string participantId)
    {
        foreach (var task in room.Tasks)
        {
            task.Votes.Remove(participantId);
        }
    }

    private static PlanningRoomState ToState(RoomKey key, PlanningRoom room)
    {
        var activeTask = ActiveTask(room);
        var isRevealed = activeTask?.IsRevealed ?? false;
        var votes = activeTask?.Votes;

        var participants = room.Participants
            .OrderBy(participant => participant.Value.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(participant =>
            {
                var hasVoted = votes is not null && votes.ContainsKey(participant.Key);
                string? vote = isRevealed && votes is not null && votes.TryGetValue(participant.Key, out var cast)
                    ? cast
                    : null;

                return new PlanningParticipantState(
                    participant.Key,
                    participant.Value.DisplayName,
                    hasVoted,
                    vote,
                    participant.Value.Connections.Count > 0);
            })
            .ToArray();

        var tasks = room.Tasks
            .OrderBy(task => task.SortOrder)
            .Select(task => new PlanningTaskState(task.TaskId, task.Title, task.SortOrder, task.AgreedEstimate))
            .ToArray();

        return new PlanningRoomState(
            key.SessionId.ToString(),
            room.CurrentTaskId,
            isRevealed,
            participants,
            tasks,
            [.. room.ScaleValues]);
    }

    private readonly record struct ConnectionOwner(RoomKey Key, string ParticipantId);

    private sealed class PlanningRoom
    {
        public bool Seeded { get; set; }

        public Guid? CurrentTaskId { get; set; }

        public List<RoomTask> Tasks { get; } = [];

        public List<string> ScaleValues { get; set; } = [];

        public Dictionary<string, Participant> Participants { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class RoomTask(Guid taskId, string title, int sortOrder)
    {
        public Guid TaskId { get; } = taskId;

        public string Title { get; } = title;

        public int SortOrder { get; } = sortOrder;

        public string? AgreedEstimate { get; set; }

        public bool IsRevealed { get; set; }

        public Dictionary<string, string> Votes { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class Participant(string displayName)
    {
        public string DisplayName { get; } = string.IsNullOrWhiteSpace(displayName) ? "Guest" : displayName;

        public HashSet<string> Connections { get; } = new(StringComparer.Ordinal);
    }
}
