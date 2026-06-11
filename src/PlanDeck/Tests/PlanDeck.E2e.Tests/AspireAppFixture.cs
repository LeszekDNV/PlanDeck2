using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace PlanDeck.E2e.Tests;

[SetUpFixture]
public class AspireAppFixture
{
    /// <summary>
    /// When set (e.g. on a CI / test / prod environment), the tests run against this
    /// already-deployed URL and Aspire is NOT started. When empty, Aspire is launched
    /// locally on the developer machine. Configured via the <c>BaseUrl</c>
    /// <c>TestRunParameters</c> entry in <c>.runsettings</c>.
    /// </summary>
    private const string BaseUrlParameter = "BaseUrl";

    private DistributedApplication? _app;

    public static string BaseUrl { get; private set; } = string.Empty;

    [OneTimeSetUp]
    public async Task StartAsync()
    {
        var externalBaseUrl = TestContext.Parameters.Get(BaseUrlParameter);
        if (!string.IsNullOrWhiteSpace(externalBaseUrl))
        {
            // Remote environment: the server is already running, go straight to it.
            BaseUrl = externalBaseUrl;
            return;
        }

        // Local developer machine: spin up the whole app via Aspire.
        var builder = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.PlanDeck_AppHost>();

        _app = await builder.BuildAsync();
        await _app.StartAsync();

        var notifications = _app.Services.GetRequiredService<ResourceNotificationService>();
        await notifications
            .WaitForResourceAsync("plandeck-server", KnownResourceStates.Running)
            .WaitAsync(TimeSpan.FromMinutes(2));

        BaseUrl = _app.GetEndpoint("plandeck-server", "https").ToString();
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
