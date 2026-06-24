using System.Runtime.Serialization;
using ProtoBuf.Grpc;
using ProtoBuf.Grpc.Configuration;

namespace PlanDeck.Core.Shared.Contracts;

[Service]
public interface IAuthService
{
    [Operation]
    Task<CurrentUserReply> GetCurrentUserAsync(CurrentUserRequest request, CallContext context = default);
}

[DataContract]
public sealed class CurrentUserRequest
{
}

[DataContract]
public sealed class CurrentUserReply
{
    [DataMember(Order = 1)]
    public bool IsAuthenticated { get; set; }

    [DataMember(Order = 2)]
    public string? DisplayName { get; set; }

    [DataMember(Order = 3)]
    public string? Email { get; set; }

    [DataMember(Order = 4)]
    public string? ParticipantId { get; set; }

    [DataMember(Order = 5)]
    public bool IsGuest { get; set; }
}
