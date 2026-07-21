using Azure;
using Azure.Core;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Caching.Memory;
using PlanDeck.Application.Abstractions;
using PlanDeck.Infrastructure.AzureDevOps;

namespace PlanDeck.Unit.Tests.AzureDevOps;

[TestFixture]
public sealed class KeyVaultProjectSecretStoreTests
{
    [TestCase(404, typeof(ProjectSecretMissingException))]
    [TestCase(403, typeof(ProjectSecretForbiddenException))]
    [TestCase(500, typeof(ProjectSecretUnavailableException))]
    public void GetLatest_RequestFailureMapsToSanitizedTypedException(
        int status,
        Type expectedType)
    {
        const string secretName = "pat-sensitive-identifier";
        var client = new ThrowingSecretClient(status, secretName);
        var store = new KeyVaultProjectSecretStore(
            client,
            TimeProvider.System,
            new MemoryCache(new MemoryCacheOptions()));

        var exception = Assert.CatchAsync<ProjectSecretStoreException>(() =>
            store.GetLatestAsync(secretName, CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(exception, Is.TypeOf(expectedType));
            Assert.That(exception!.Message, Does.Not.Contain(secretName));
            Assert.That(exception.Message, Does.Not.Contain("upstream-sensitive-detail"));
        });
    }

    private sealed class ThrowingSecretClient(int status, string secretName)
        : SecretClient(
            new Uri("https://unit-test.vault.azure.net"),
            StubCredential.Instance)
    {
        public override Task<Response<KeyVaultSecret>> GetSecretAsync(
            string name,
            string? version = null,
            CancellationToken cancellationToken = default)
        {
            Assert.That(name, Is.EqualTo(secretName));
            throw new RequestFailedException(
                status,
                $"upstream-sensitive-detail {secretName}");
        }
    }

    private sealed class StubCredential : TokenCredential
    {
        public static StubCredential Instance { get; } = new();

        public override AccessToken GetToken(
            TokenRequestContext requestContext,
            CancellationToken cancellationToken) =>
            new("token", DateTimeOffset.MaxValue);

        public override ValueTask<AccessToken> GetTokenAsync(
            TokenRequestContext requestContext,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(new AccessToken("token", DateTimeOffset.MaxValue));
    }
}
