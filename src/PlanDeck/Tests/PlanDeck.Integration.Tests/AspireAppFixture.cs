using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;

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

        // Wait for the server: its Development startup applies the migration, so by the
        // time it is Running the PlanDeckDb schema exists and tests avoid a DDL race.
        await notifications
            .WaitForResourceAsync("plandeck-server", KnownResourceStates.Running)
            .WaitAsync(TimeSpan.FromMinutes(5));

        ConnectionString = await _app.GetConnectionStringAsync("PlanDeckDb")
            ?? throw new InvalidOperationException("Connection string 'PlanDeckDb' was not provided by the AppHost.");
    }

    [OneTimeTearDown]
    public async Task StopAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }
}
