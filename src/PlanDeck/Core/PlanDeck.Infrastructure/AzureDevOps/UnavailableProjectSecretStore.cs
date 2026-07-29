using PlanDeck.Application.Abstractions;

namespace PlanDeck.Infrastructure.AzureDevOps;

public sealed class UnavailableProjectSecretStore : IProjectSecretStore
{
    public Task<string> CreateAsync(
        string value,
        CancellationToken cancellationToken) =>
        Task.FromException<string>(new ProjectSecretUnavailableException());

    public Task<string> GetLatestAsync(
        string secretName,
        CancellationToken cancellationToken) =>
        Task.FromException<string>(new ProjectSecretUnavailableException());

    public Task RotateAsync(
        string secretName,
        string value,
        CancellationToken cancellationToken) =>
        Task.FromException(new ProjectSecretUnavailableException());

    public Task SoftDeleteAsync(
        string secretName,
        CancellationToken cancellationToken) =>
        Task.FromException(new ProjectSecretUnavailableException());

    public Task RecoverAsync(
        string secretName,
        CancellationToken cancellationToken) =>
        Task.FromException(new ProjectSecretUnavailableException());

    public void Invalidate(string secretName)
    {
    }
}
