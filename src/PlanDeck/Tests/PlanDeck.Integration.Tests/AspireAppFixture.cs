using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.EntityFrameworkCore;
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

    [OneTimeSetUp]
    public async Task StartAsync()
    {
        var builder = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.PlanDeck_AppHost>();

        _app = await builder.BuildAsync();
        await _app.StartAsync();

        var notifications = _app.Services.GetRequiredService<ResourceNotificationService>();

        await notifications
            .WaitForResourceAsync("plandeck-server", KnownResourceStates.Running)
            .WaitAsync(TimeSpan.FromMinutes(5));

        ConnectionString = await _app.GetConnectionStringAsync("PlanDeckDb")
            ?? throw new InvalidOperationException("Connection string 'PlanDeckDb' was not provided by the AppHost.");

        var options = new DbContextOptionsBuilder<PlanDeckDbContext>()
            .UseSqlServer(ConnectionString, sql => sql.EnableRetryOnFailure())
            .Options;
        await using var db = new PlanDeckDbContext(options, MigrationUserContext.Instance);
        await db.Database.MigrateAsync();
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
