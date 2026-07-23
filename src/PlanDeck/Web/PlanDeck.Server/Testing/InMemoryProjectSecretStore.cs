using System.Collections.Concurrent;
using PlanDeck.Application.Abstractions;

namespace PlanDeck.Server.Testing;

public sealed class InMemoryProjectSecretStore : IProjectSecretStore
{
    private readonly ConcurrentDictionary<string, string> _secrets = new(StringComparer.Ordinal);

    public Task<string> CreateAsync(string value, CancellationToken cancellationToken)
    {
        var name = $"test-{Guid.NewGuid():N}";
        _secrets[name] = value;
        return Task.FromResult(name);
    }

    public Task<string> GetLatestAsync(string secretName, CancellationToken cancellationToken)
    {
        if (_secrets.TryGetValue(secretName, out var value))
        {
            return Task.FromResult(value);
        }

        throw new ProjectSecretMissingException();
    }

    public Task RotateAsync(string secretName, string value, CancellationToken cancellationToken)
    {
        if (!_secrets.ContainsKey(secretName))
        {
            throw new ProjectSecretMissingException();
        }

        _secrets[secretName] = value;
        return Task.CompletedTask;
    }

    public Task SoftDeleteAsync(string secretName, CancellationToken cancellationToken)
    {
        _secrets.TryRemove(secretName, out _);
        return Task.CompletedTask;
    }

    public void Invalidate(string secretName)
    {
    }
}
