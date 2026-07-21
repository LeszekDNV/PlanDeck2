using PlanDeck.Core.Shared.Contracts;

namespace PlanDeck.Client.Services;

public interface IProjectClientService
{
    Task<IReadOnlyList<ProjectDto>> GetProjectsAsync();

    Task<ProjectDto> CreateProjectAsync(string name, string? description);
}
