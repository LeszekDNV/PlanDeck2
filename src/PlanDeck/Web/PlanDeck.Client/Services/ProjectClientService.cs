using Grpc.Net.Client;
using PlanDeck.Core.Shared.Contracts;
using ProtoBuf.Grpc.Client;

namespace PlanDeck.Client.Services;

public sealed class ProjectClientService(GrpcChannel channel) : IProjectClientService
{
    public async Task<IReadOnlyList<ProjectDto>> GetProjectsAsync()
    {
        var service = channel.CreateGrpcService<IProjectService>();
        var reply = await service.ListProjectsAsync(new ListProjectsRequest());
        return reply.Projects;
    }

    public async Task<ProjectDto> CreateProjectAsync(string name, string? description)
    {
        var service = channel.CreateGrpcService<IProjectService>();
        var reply = await service.CreateProjectAsync(new CreateProjectRequest
        {
            Name = name,
            Description = description
        });
        return reply.Project;
    }
}
