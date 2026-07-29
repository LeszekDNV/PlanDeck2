using PlanDeck.Application.Abstractions;
using PlanDeck.Infrastructure.AzureDevOps;

namespace PlanDeck.Unit.Tests.AzureDevOps;

[TestFixture]
public sealed class UnavailableProjectSecretStoreTests
{
    private readonly UnavailableProjectSecretStore _store = new();

    [Test]
    public void Create_ThrowsUnavailable()
    {
        Assert.ThrowsAsync<ProjectSecretUnavailableException>(() =>
            _store.CreateAsync("value", CancellationToken.None));
    }

    [Test]
    public void GetLatest_ThrowsUnavailable()
    {
        Assert.ThrowsAsync<ProjectSecretUnavailableException>(() =>
            _store.GetLatestAsync("secret", CancellationToken.None));
    }

    [Test]
    public void Invalidate_DoesNotThrow()
    {
        Assert.DoesNotThrow(() => _store.Invalidate("secret"));
    }
}
