using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PlanDeck.Application.Abstractions;
using PlanDeck.Infrastructure.Persistence;

namespace PlanDeck.Integration.Tests;

[SetUpFixture]
public class AspireAppFixture
{
    private DistributedApplication? _app;

    /// <summary>
    /// Connection string for the Aspire-provisioned <c>PlanDeckDb</c> SQL Server
    /// database, available once the AppHost has started locally.
    /// </summary>
    public static string ConnectionString { get; private set; } = string.Empty;

    public static string KeyVaultUri { get; private set; } = string.Empty;

    [OneTimeSetUp]
    public async Task StartAsync()
    {
        var builder = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.PlanDeck_AppHost>();
        EnsureAzureProvisioningConfigured(builder.Configuration);

        _app = await builder.BuildAsync();
        await _app.StartAsync();

        var notifications = _app.Services.GetRequiredService<ResourceNotificationService>();

        await notifications
            .WaitForResourceAsync("key-vault", KnownResourceStates.Running)
            .WaitAsync(TimeSpan.FromMinutes(5));
        await notifications
            .WaitForResourceAsync("plandeck-server", KnownResourceStates.Running)
            .WaitAsync(TimeSpan.FromMinutes(5));

        ConnectionString = await _app.GetConnectionStringAsync("PlanDeckDb")
            ?? throw new InvalidOperationException("Connection string 'PlanDeckDb' was not provided by the AppHost.");
        KeyVaultUri = await _app.GetConnectionStringAsync("key-vault")
            ?? throw new InvalidOperationException("Connection string 'key-vault' was not provided by the AppHost.");

        var options = new DbContextOptionsBuilder<PlanDeckDbContext>()
            .UseSqlServer(ConnectionString, sql => sql.EnableRetryOnFailure())
            .Options;
        await using var db = new PlanDeckDbContext(options, MigrationUserContext.Instance);
        await db.Database.MigrateAsync();
    }

    private static void EnsureAzureProvisioningConfigured(IConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(configuration["Azure:SubscriptionId"])
            || string.IsNullOrWhiteSpace(configuration["Azure:Location"]))
        {
            throw new InvalidOperationException(
                "Local Aspire integration tests require Azure:SubscriptionId and Azure:Location for a dedicated non-production Key Vault.");
        }
    }

    [OneTimeTearDown]
    public async Task StopAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }

    private sealed class MigrationUserContext : ICurrentUserContext
    {
        public static MigrationUserContext Instance { get; } = new();

        public Guid TenantId => Guid.Empty;

        public Guid UserId => Guid.Empty;

        public bool IsAuthenticated => false;

        public string? DisplayName => null;

        public string? Email => null;
    }
}
