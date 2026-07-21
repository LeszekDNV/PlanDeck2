using System.Collections.Concurrent;
using PlanDeck.Application.Abstractions;
using PlanDeck.Core.Shared.Realtime;

namespace PlanDeck.Application.Planning;

public sealed class PlanningRoomService : IPlanningRoomService
{
    private readonly ConcurrentDictionary<RoomKey, PlanningRoom> _rooms = new();
    private readonly ConcurrentDictionary<string, ConnectionOwner> _connections = new(StringComparer.Ordinal);

    public PlanningRoomState EnsureSeeded(
        RoomKey key,
        IReadOnlyList<PlanningRoomTaskSnapshot> tasks,
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
                        Description = task.Description,
                        AgreedEstimate = task.AgreedEstimate
                    });
                }

                room.CurrentTaskId = room.Tasks.OrderBy(t => t.SortOrder).FirstOrDefault()?.TaskId;
                room.Seeded = true;
                room.Revision++;
            }

            return ToState(key, room);
        }
    }

    public PlanningRoomState SyncTasks(RoomKey key, IReadOnlyList<PlanningRoomTaskSnapshot> tasks)
    {
        ArgumentNullException.ThrowIfNull(tasks);

        var room = GetRoom(key);
        lock (room)
        {
            // An unseeded room has no connected participants: skip reconciliation so the
            // first JoinRoom seeds fresh from the database without duplicating tasks.
            if (!room.Seeded)
            {
                return ToState(key, room);
            }

            var incomingIds = tasks.Select(t => t.TaskId).ToHashSet();
            var activeRemoved = room.CurrentTaskId is Guid current && !incomingIds.Contains(current);
            var changed = room.Tasks.Any(t => !incomingIds.Contains(t.TaskId));
            room.Tasks.RemoveAll(t => !incomingIds.Contains(t.TaskId));

            foreach (var snapshot in tasks)
            {
                var existing = room.Tasks.FirstOrDefault(t => t.TaskId == snapshot.TaskId);
                if (existing is null)
                {
                    changed = true;
                    room.Tasks.Add(new RoomTask(snapshot.TaskId, snapshot.Title, snapshot.SortOrder)
                    {
                        Description = snapshot.Description,
                        AgreedEstimate = snapshot.AgreedEstimate
                    });
                }
                else
                {
                    changed |= existing.Title != snapshot.Title
                        || existing.Description != snapshot.Description
                        || existing.SortOrder != snapshot.SortOrder;
                    // Preserve in-flight Votes/IsRevealed/AgreedEstimate; only metadata changes.
                    existing.Title = snapshot.Title;
                    existing.Description = snapshot.Description;
                    existing.SortOrder = snapshot.SortOrder;
                }
            }

            if (activeRemoved)
            {
                room.CurrentTaskId = room.Tasks.OrderBy(t => t.SortOrder).FirstOrDefault()?.TaskId;
            }

            if (changed)
            {
                room.Revision++;
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

        var owner = new ConnectionOwner(key, participantId);
        var reserved = ReserveConnection(connectionId, owner);

        try
        {
            var room = GetRoom(key);
            lock (room)
            {
                var changed = false;
                if (!room.Participants.TryGetValue(participantId, out var participant))
                {
                    participant = new Participant(displayName);
                    room.Participants[participantId] = participant;
                    changed = true;
                }

                var wasOnline = participant.Connections.Count > 0;
                participant.Connections.Add(connectionId);
                if (!wasOnline)
                {
                    changed = true;
                }

                if (changed)
                {
                    room.Revision++;
                }

                return ToState(key, room);
            }
        }
        catch
        {
            if (reserved)
            {
                _connections.TryRemove(new KeyValuePair<string, ConnectionOwner>(connectionId, owner));
            }

            throw;
        }
    }

    public PlanningRoomState Leave(RoomKey key, string participantId, string connectionId)
    {
        var owner = new ConnectionOwner(key, participantId);
        if (_connections.TryGetValue(connectionId, out var currentOwner) && currentOwner != owner)
        {
            throw new InvalidOperationException(
                "Connection belongs to a different planning room or participant.");
        }

        var room = GetRoom(key);
        lock (room)
        {
            _connections.TryRemove(new KeyValuePair<string, ConnectionOwner>(connectionId, owner));

            if (room.Participants.TryGetValue(participantId, out var participant))
            {
                participant.Connections.Remove(connectionId);
                if (participant.Connections.Count == 0)
                {
                    room.Participants.Remove(participantId);
                    RemoveVotes(room, participantId);
                    room.Revision++;
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
                var wasOnline = participant.Connections.Count > 0;
                participant.Connections.Remove(connectionId);
                if (wasOnline && participant.Connections.Count == 0)
                {
                    room.Revision++;
                }
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

            if (!task.Votes.TryGetValue(participantId, out var currentVote)
                || !string.Equals(currentVote, vote, StringComparison.Ordinal))
            {
                task.Votes[participantId] = vote;
                room.Revision++;
            }

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

            if (!task.IsRevealed)
            {
                task.IsRevealed = true;
                room.Revision++;
            }

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

            if (task.IsRevealed || task.Votes.Count > 0 || task.AgreedEstimate is not null)
            {
                task.IsRevealed = false;
                task.Votes.Clear();
                task.AgreedEstimate = null;
                room.Revision++;
            }

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

            if (room.CurrentTaskId != taskId)
            {
                room.CurrentTaskId = taskId;
                room.Revision++;
            }

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

            if (!string.Equals(task.AgreedEstimate, estimate, StringComparison.Ordinal))
            {
                task.AgreedEstimate = estimate;
                room.Revision++;
            }

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

    private bool ReserveConnection(string connectionId, ConnectionOwner owner)
    {
        while (!_connections.TryAdd(connectionId, owner))
        {
            if (!_connections.TryGetValue(connectionId, out var currentOwner))
            {
                continue;
            }

            if (currentOwner == owner)
            {
                return false;
            }

            throw new InvalidOperationException(
                "Connection is already joined to a different planning room or participant.");
        }

        return true;
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
            .Select(task => new PlanningTaskState(task.TaskId, task.Title, task.Description, task.SortOrder, task.AgreedEstimate))
            .ToArray();

        return new PlanningRoomState(
            key.SessionId.ToString(),
            room.CurrentTaskId,
            isRevealed,
            participants,
            tasks,
            [.. room.ScaleValues],
            room.Revision);
    }

    private readonly record struct ConnectionOwner(RoomKey Key, string ParticipantId);

    private sealed class PlanningRoom
    {
        public bool Seeded { get; set; }

        public long Revision { get; set; }

        public Guid? CurrentTaskId { get; set; }

        public List<RoomTask> Tasks { get; } = [];

        public List<string> ScaleValues { get; set; } = [];

        public Dictionary<string, Participant> Participants { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class RoomTask(Guid taskId, string title, int sortOrder)
    {
        public Guid TaskId { get; } = taskId;

        public string Title { get; set; } = title;

        public string? Description { get; set; }

        public int SortOrder { get; set; } = sortOrder;

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
