using PlanDeck.Core.Shared.Contracts;
using ProtoBuf.Grpc;

namespace PlanDeck.Application.Services;

public class HelloGrpcService : IHelloService
{
    public Task<HelloReply> SayHelloAsync(HelloRequest request, CallContext context = default)
    {
        return Task.FromResult(new HelloReply { Message = "Hello World!" });
    }
}
