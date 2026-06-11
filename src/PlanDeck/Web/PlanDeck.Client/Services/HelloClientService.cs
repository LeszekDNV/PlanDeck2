using Grpc.Net.Client;
using PlanDeck.Core.Shared.Contracts;
using ProtoBuf.Grpc.Client;

namespace PlanDeck.Client.Services;

public class HelloClientService : IHelloClientService
{
    private readonly GrpcChannel _channel;

    public HelloClientService(GrpcChannel channel)
    {
        _channel = channel;
    }

    public async Task<string> GetHelloAsync()
    {
        var service = _channel.CreateGrpcService<IHelloService>();
        var reply = await service.SayHelloAsync(new HelloRequest());
        return reply.Message;
    }
}
