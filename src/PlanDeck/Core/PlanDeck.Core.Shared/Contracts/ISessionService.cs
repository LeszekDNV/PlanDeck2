using System.Runtime.Serialization;
using ProtoBuf.Grpc;
using ProtoBuf.Grpc.Configuration;

namespace PlanDeck.Core.Shared.Contracts;

[Service]
public interface ISessionService
{
    [Operation]
    Task<CreateSessionReply> CreateSessionAsync(CreateSessionRequest request, CallContext context = default);

    [Operation]
    Task<ListSessionsReply> ListSessionsAsync(ListSessionsRequest request, CallContext context = default);

    [Operation]
    Task<GetSessionReply> GetSessionAsync(GetSessionRequest request, CallContext context = default);

    [Operation]
    Task<UpdateSessionConfigReply> UpdateSessionConfigAsync(UpdateSessionConfigRequest request, CallContext context = default);

    [Operation]
    Task<AddTaskReply> AddTaskAsync(AddTaskRequest request, CallContext context = default);

    [Operation]
    Task<AddTasksReply> AddTasksAsync(AddTasksRequest request, CallContext context = default);

    [Operation]
    Task<UpdateTaskReply> UpdateTaskAsync(UpdateTaskRequest request, CallContext context = default);

    [Operation]
    Task<RemoveTaskReply> RemoveTaskAsync(RemoveTaskRequest request, CallContext context = default);

    [Operation]
    Task<ActivateSessionReply> ActivateSessionAsync(ActivateSessionRequest request, CallContext context = default);

    [Operation]
    Task<DeleteSessionReply> DeleteSessionAsync(DeleteSessionRequest request, CallContext context = default);
}

[DataContract]
public enum SessionStatusDto
{
    [EnumMember] Draft = 0,
    [EnumMember] Active = 1
}

[DataContract]
public enum VotingScaleTypeDto
{
    [EnumMember] Fibonacci = 0,
    [EnumMember] TShirt = 1,
    [EnumMember] Custom = 2
}

[DataContract]
public enum TaskSourceDto
{
    [EnumMember] AdHoc = 0,
    [EnumMember] AzureDevOps = 1
}

[DataContract]
public sealed class SessionDto
{
    [DataMember(Order = 1)]
    public Guid Id { get; set; }

    [DataMember(Order = 2)]
    public string Name { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public Guid? TeamId { get; set; }

    [DataMember(Order = 4)]
    public SessionStatusDto Status { get; set; }

    [DataMember(Order = 5)]
    public VotingScaleTypeDto ScaleType { get; set; }

    [DataMember(Order = 6)]
    public List<string> ScaleValues { get; set; } = [];

    [DataMember(Order = 7)]
    public DateTime CreatedAtUtc { get; set; }

    [DataMember(Order = 8)]
    public List<SessionTaskDto> Tasks { get; set; } = [];

    [DataMember(Order = 9)]
    public string? ShareCode { get; set; }
}

[DataContract]
public sealed class SessionTaskDto
{
    [DataMember(Order = 1)]
    public Guid Id { get; set; }

    [DataMember(Order = 2)]
    public string Title { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public TaskSourceDto Source { get; set; }

    [DataMember(Order = 4)]
    public int SortOrder { get; set; }

    [DataMember(Order = 5)]
    public int? AdoWorkItemId { get; set; }

    [DataMember(Order = 6)]
    public int? AdoRevision { get; set; }

    [DataMember(Order = 7)]
    public string? WorkItemType { get; set; }

    [DataMember(Order = 8)]
    public string? State { get; set; }

    [DataMember(Order = 9)]
    public string? AgreedEstimate { get; set; }

    [DataMember(Order = 10)]
    public string? Description { get; set; }
}

[DataContract]
public sealed class NewSessionTaskDto
{
    [DataMember(Order = 1)]
    public string Title { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public TaskSourceDto Source { get; set; }

    [DataMember(Order = 3)]
    public int? AdoWorkItemId { get; set; }

    [DataMember(Order = 4)]
    public int? AdoRevision { get; set; }

    [DataMember(Order = 5)]
    public string? WorkItemType { get; set; }

    [DataMember(Order = 6)]
    public string? State { get; set; }

    [DataMember(Order = 7)]
    public string? Description { get; set; }
}

[DataContract]
public sealed class CreateSessionRequest
{
    [DataMember(Order = 1)]
    public string Name { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public Guid? TeamId { get; set; }

    [DataMember(Order = 3)]
    public VotingScaleTypeDto ScaleType { get; set; }

    [DataMember(Order = 4)]
    public List<string> CustomScaleValues { get; set; } = [];

    [DataMember(Order = 5)]
    public List<NewSessionTaskDto> Tasks { get; set; } = [];
}

[DataContract]
public sealed class CreateSessionReply
{
    [DataMember(Order = 1)]
    public SessionDto Session { get; set; } = new();
}

[DataContract]
public sealed class ListSessionsRequest
{
}

[DataContract]
public sealed class ListSessionsReply
{
    [DataMember(Order = 1)]
    public List<SessionDto> Sessions { get; set; } = [];
}

[DataContract]
public sealed class GetSessionRequest
{
    [DataMember(Order = 1)]
    public Guid Id { get; set; }
}

[DataContract]
public sealed class GetSessionReply
{
    [DataMember(Order = 1)]
    public SessionDto Session { get; set; } = new();
}

[DataContract]
public sealed class UpdateSessionConfigRequest
{
    [DataMember(Order = 1)]
    public Guid Id { get; set; }

    [DataMember(Order = 2)]
    public string Name { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public VotingScaleTypeDto ScaleType { get; set; }

    [DataMember(Order = 4)]
    public List<string> CustomScaleValues { get; set; } = [];

    [DataMember(Order = 5)]
    public Guid? TeamId { get; set; }
}

[DataContract]
public sealed class UpdateSessionConfigReply
{
    [DataMember(Order = 1)]
    public SessionDto Session { get; set; } = new();
}

[DataContract]
public sealed class AddTaskRequest
{
    [DataMember(Order = 1)]
    public Guid SessionId { get; set; }

    [DataMember(Order = 2)]
    public NewSessionTaskDto Task { get; set; } = new();
}

[DataContract]
public sealed class AddTaskReply
{
    [DataMember(Order = 1)]
    public SessionDto Session { get; set; } = new();
}

[DataContract]
public sealed class AddTasksRequest
{
    [DataMember(Order = 1)]
    public Guid SessionId { get; set; }

    [DataMember(Order = 2)]
    public List<NewSessionTaskDto> Tasks { get; set; } = [];
}

[DataContract]
public sealed class AddTasksReply
{
    [DataMember(Order = 1)]
    public SessionDto Session { get; set; } = new();
}

[DataContract]
public sealed class UpdateTaskRequest
{
    [DataMember(Order = 1)]
    public Guid SessionId { get; set; }

    [DataMember(Order = 2)]
    public Guid TaskId { get; set; }

    [DataMember(Order = 3)]
    public string Title { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string? Description { get; set; }
}

[DataContract]
public sealed class UpdateTaskReply
{
    [DataMember(Order = 1)]
    public SessionDto Session { get; set; } = new();
}

[DataContract]
public sealed class RemoveTaskRequest
{
    [DataMember(Order = 1)]
    public Guid SessionId { get; set; }

    [DataMember(Order = 2)]
    public Guid TaskId { get; set; }
}

[DataContract]
public sealed class RemoveTaskReply
{
    [DataMember(Order = 1)]
    public SessionDto Session { get; set; } = new();
}

[DataContract]
public sealed class ActivateSessionRequest
{
    [DataMember(Order = 1)]
    public Guid Id { get; set; }
}

[DataContract]
public sealed class ActivateSessionReply
{
    [DataMember(Order = 1)]
    public SessionDto Session { get; set; } = new();
}

[DataContract]
public sealed class DeleteSessionRequest
{
    [DataMember(Order = 1)]
    public Guid Id { get; set; }
}

[DataContract]
public sealed class DeleteSessionReply
{
    [DataMember(Order = 1)]
    public bool Deleted { get; set; }
}
