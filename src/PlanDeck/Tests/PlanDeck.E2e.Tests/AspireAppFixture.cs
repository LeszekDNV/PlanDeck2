using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace PlanDeck.E2e.Tests;

[SetUpFixture]
public class AspireAppFixture
{
    /// <summary>
    /// When set for an explicitly identified non-production environment, the tests run against this
    /// already-deployed URL and Aspire is NOT started. When empty, Aspire is launched
    /// locally on the developer machine. Configured via the <c>BaseUrl</c>
    /// <c>TestRunParameters</c> entry in <c>.runsettings</c>.
    /// </summary>
    private const string BaseUrlParameter = "BaseUrl";
    private const string RemoteEnvironmentParameter = "RemoteEnvironment";

    private DistributedApplication? _app;

    public static string BaseUrl { get; private set; } = string.Empty;

    [OneTimeSetUp]
    public async Task StartAsync()
    {
        var externalBaseUrl = TestContext.Parameters.Get(BaseUrlParameter);
        if (!string.IsNullOrWhiteSpace(externalBaseUrl))
        {
            var remoteEnvironment = TestContext.Parameters.Get(RemoteEnvironmentParameter);
            if (!string.Equals(remoteEnvironment, "Test", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(remoteEnvironment, "Staging", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Remote E2E requires RemoteEnvironment=Test or Staging. Production targets are not allowed.");
            }

            // Remote environment: the server is already running, go straight to it.
            BaseUrl = externalBaseUrl;
            return;
        }

        // Local developer machine: spin up the whole app via Aspire.
        // Drive the deterministic test-auth scheme so the browser is auto-authenticated
        // (env-gated in the AppHost; has no effect on a normal `dotnet run`).
        Environment.SetEnvironmentVariable("PLANDECK_E2E_TESTAUTH", "true");

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

        BaseUrl = _app.GetEndpoint("plandeck-server", "https").ToString();
    }

    private static void EnsureAzureProvisioningConfigured(IConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(configuration["Azure:SubscriptionId"])
            || string.IsNullOrWhiteSpace(configuration["Azure:Location"]))
        {
            throw new InvalidOperationException(
                "Local E2E requires Azure:SubscriptionId and Azure:Location for a dedicated non-production Key Vault.");
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
}
