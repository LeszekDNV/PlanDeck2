using System.Runtime.Serialization;
using ProtoBuf.Grpc;
using ProtoBuf.Grpc.Configuration;

namespace PlanDeck.Core.Shared.Contracts;

[Service]
public interface IHelloService
{
    [Operation]
    Task<HelloReply> SayHelloAsync(HelloRequest request, CallContext context = default);
}

[DataContract]
public class HelloRequest
{
    [DataMember(Order = 1)]
    public string Name { get; set; } = string.Empty;
}

[DataContract]
public class HelloReply
{
    [DataMember(Order = 1)]
    public string Message { get; set; } = string.Empty;
}
