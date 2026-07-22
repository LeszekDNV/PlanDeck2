using Azure;
using Azure.Security.KeyVault.Secrets;
using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;
using PlanDeck.Application.Abstractions;

namespace PlanDeck.Infrastructure.AzureDevOps;

public sealed class KeyVaultProjectSecretStore(
    SecretClient client,
    TimeProvider timeProvider,
    IMemoryCache cache) : IProjectSecretStore
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(2);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _secretLocks = new();

    public async Task<string> CreateAsync(
        string value,
        CancellationToken cancellationToken)
    {
        var secretName = $"pat-{Guid.NewGuid():N}";
        try
        {
            await client.SetSecretAsync(secretName, value, cancellationToken);
            return secretName;
        }
        catch (RequestFailedException exception)
        {
            throw Map(exception);
        }
        catch (Exception exception) when (IsAuthenticationFailure(exception))
        {
            throw new ProjectSecretUnavailableException();
        }
    }

    public async Task<string> GetLatestAsync(
        string secretName,
        CancellationToken cancellationToken)
    {
        var secretLock = _secretLocks.GetOrAdd(secretName, _ => new SemaphoreSlim(1, 1));
        await secretLock.WaitAsync(cancellationToken);
        try
        {
            if (cache.TryGetValue<CacheEntry>(secretName, out var cached)
                && cached.ExpiresAtUtc > timeProvider.GetUtcNow())
            {
                return cached.Value;
            }

            cache.Remove(secretName);
            var response = await client.GetSecretAsync(
                secretName,
                version: null,
                cancellationToken);
            var value = response.Value.Value;
            cache.Set(
                secretName,
                new CacheEntry(
                    value,
                    timeProvider.GetUtcNow().Add(CacheLifetime)),
                CacheLifetime);
            return value;
        }
        catch (RequestFailedException exception)
        {
            throw Map(exception);
        }
        catch (Exception exception) when (IsAuthenticationFailure(exception))
        {
            throw new ProjectSecretUnavailableException();
        }
        finally
        {
            secretLock.Release();
        }
    }

    public async Task RotateAsync(
        string secretName,
        string value,
        CancellationToken cancellationToken)
    {
        var secretLock = _secretLocks.GetOrAdd(secretName, _ => new SemaphoreSlim(1, 1));
        await secretLock.WaitAsync(cancellationToken);
        try
        {
            await client.SetSecretAsync(secretName, value, cancellationToken);
            Invalidate(secretName);
        }
        catch (RequestFailedException exception)
        {
            throw Map(exception);
        }
        catch (Exception exception) when (IsAuthenticationFailure(exception))
        {
            throw new ProjectSecretUnavailableException();
        }
        finally
        {
            secretLock.Release();
        }
    }

    public async Task SoftDeleteAsync(
        string secretName,
        CancellationToken cancellationToken)
    {
        var secretLock = _secretLocks.GetOrAdd(secretName, _ => new SemaphoreSlim(1, 1));
        await secretLock.WaitAsync(cancellationToken);
        try
        {
            Invalidate(secretName);
            await client.StartDeleteSecretAsync(secretName, cancellationToken);
        }
        catch (RequestFailedException exception)
            when (exception.Status == 404
                || exception is { Status: 409, ErrorCode: "SecretBeingDeleted" })
        {
            // Soft deletion is intentionally idempotent so a failed SQL cleanup can be retried.
        }
        catch (RequestFailedException exception)
        {
            throw Map(exception);
        }
        catch (Exception exception) when (IsAuthenticationFailure(exception))
        {
            throw new ProjectSecretUnavailableException();
        }
        finally
        {
            secretLock.Release();
        }
    }

    public void Invalidate(string secretName) => cache.Remove(secretName);

    private static ProjectSecretStoreException Map(RequestFailedException exception) =>
        exception.Status switch
        {
            404 => new ProjectSecretMissingException(),
            401 or 403 => new ProjectSecretForbiddenException(),
            _ => new ProjectSecretUnavailableException()
        };

    private static bool IsAuthenticationFailure(Exception exception) =>
        string.Equals(
            exception.GetType().FullName,
            "Azure.Identity.AuthenticationFailedException",
            StringComparison.Ordinal);

    private sealed record CacheEntry(string Value, DateTimeOffset ExpiresAtUtc);
}
