using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Caching.Memory;
using PlanDeck.Application.Abstractions;
using PlanDeck.Infrastructure.AzureDevOps;

// Outside PlanDeck.Integration.Tests so this dedicated vault test does not start SQL or Mailpit.
// The URI must identify a vault provisioned by the PlanDeck AppHost in a non-production scope.
namespace PlanDeck.KeyVault.IntegrationTests;

[TestFixture]
public sealed class RealKeyVaultProjectSecretStoreTests
{
    [Test]
    public async Task CreateReadRotateAndSoftDelete_UsesAspireProvisionedVault()
    {
        RequireExplicitNonProductionOptIn();
        var client = new SecretClient(
            GetVaultUri(),
            new DefaultAzureCredential(new DefaultAzureCredentialOptions
            {
                ExcludeManagedIdentityCredential = true,
                ExcludeWorkloadIdentityCredential = true
            }));
        IProjectSecretStore store = new KeyVaultProjectSecretStore(
            client,
            TimeProvider.System,
            new MemoryCache(new MemoryCacheOptions()));
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

    private static void RequireExplicitNonProductionOptIn()
    {
        var runRealVaultTests = string.Equals(
            Environment.GetEnvironmentVariable("PLANDECK_RUN_REAL_KEYVAULT_TESTS"),
            "true",
            StringComparison.OrdinalIgnoreCase);
        var requireRealVaultTests = string.Equals(
            Environment.GetEnvironmentVariable("PLANDECK_REQUIRE_REAL_KEYVAULT_TESTS"),
            "true",
            StringComparison.OrdinalIgnoreCase);

        if (!runRealVaultTests)
        {
            if (requireRealVaultTests)
            {
                Assert.Fail(
                    "PLANDECK_REQUIRE_REAL_KEYVAULT_TESTS=true requires PLANDECK_RUN_REAL_KEYVAULT_TESTS=true.");
            }

            Assert.Ignore(
                "Set PLANDECK_RUN_REAL_KEYVAULT_TESTS=true to run against the Aspire-provisioned vault.");
        }

        var environment = Environment.GetEnvironmentVariable(
            "PLANDECK_KEYVAULT_TEST_ENVIRONMENT");
        if (!string.Equals(environment, "Development", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(environment, "Test", StringComparison.OrdinalIgnoreCase))
        {
            Assert.Fail(
                "PLANDECK_KEYVAULT_TEST_ENVIRONMENT must be Development or Test. Production vault tests are forbidden.");
        }
    }

    private static Uri GetVaultUri()
    {
        var value = Environment.GetEnvironmentVariable("PLANDECK_KEYVAULT_URI");
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !uri.Host.EndsWith(".vault.azure.net", StringComparison.OrdinalIgnoreCase))
        {
            Assert.Fail(
                "PLANDECK_KEYVAULT_URI must be the HTTPS URI of the Aspire-provisioned non-production vault.");
        }

        return uri!;
    }
}
