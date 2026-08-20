using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Caching.Memory;
using PlanDeck.Application.Abstractions;
using PlanDeck.Infrastructure.AzureDevOps;

namespace PlanDeck.Integration.Tests.AzureDevOps;

[TestFixture]
public sealed class RealKeyVaultProjectSecretStoreTests
{
    [Test]
    public async Task CreateReadRotateAndSoftDelete_UsesAspireProvisionedVault()
    {
        var client = new SecretClient(
            GetAspireVaultUri(),
            new AzureCliCredential());
        using var cache = new MemoryCache(new MemoryCacheOptions());
        IProjectSecretStore store = new KeyVaultProjectSecretStore(
            client,
            TimeProvider.System,
            cache);
        string? secretName = null;

        try
        {
            var originalValue = $"integration-{Guid.NewGuid():N}";
            var rotatedValue = $"rotated-{Guid.NewGuid():N}";
            secretName = await store.CreateAsync(originalValue, CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(secretName, Does.StartWith("pat-"));
                Assert.That(secretName, Has.Length.EqualTo(36));
            });
            Assert.That(
                await store.GetLatestAsync(secretName, CancellationToken.None),
                Is.EqualTo(originalValue));

            await store.RotateAsync(secretName, rotatedValue, CancellationToken.None);
            store.Invalidate(secretName);

            Assert.That(
                await store.GetLatestAsync(secretName, CancellationToken.None),
                Is.EqualTo(rotatedValue));

            await store.SoftDeleteAsync(secretName, CancellationToken.None);
            secretName = null;
        }
        finally
        {
            if (secretName is not null)
            {
                await store.SoftDeleteAsync(secretName, CancellationToken.None);
            }
        }
    }

    private static Uri GetAspireVaultUri()
    {
        var value = AspireAppFixture.KeyVaultUri;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !uri.Host.EndsWith(".vault.azure.net", StringComparison.OrdinalIgnoreCase))
        {
            Assert.Fail(
                "Aspire must provide the HTTPS URI of its non-production Key Vault.");
        }

        return uri!;
    }
}
