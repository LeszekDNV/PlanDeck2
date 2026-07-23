using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace PlanDeck.E2e.Tests;

[SetUpFixture]
public class AspireAppFixture
{
    private const string BaseUrlParameter = "BaseUrl";
    private const string RemoteEnvironmentParameter = "RemoteEnvironment";
    private const string E2eScenarioTokenParameter = "E2eScenarioToken";
    private const string LocalScenarioToken = "local-e2e-scenario-token";

    private DistributedApplication? _app;

    public static string BaseUrl { get; private set; } = string.Empty;
    public static string E2eScenarioToken { get; private set; } = string.Empty;

    [OneTimeSetUp]
    public async Task StartAsync()
    {
        var externalBaseUrl = TestContext.Parameters.Get(BaseUrlParameter);
        if (!string.IsNullOrWhiteSpace(externalBaseUrl))
        {
            var remoteEnvironment = TestContext.Parameters.Get(RemoteEnvironmentParameter);
            if (!string.Equals(remoteEnvironment, "Test", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Remote E2E requires RemoteEnvironment=Test.");
            }

            var scenarioToken = TestContext.Parameters.Get(E2eScenarioTokenParameter);
            if (string.IsNullOrWhiteSpace(scenarioToken))
            {
                throw new InvalidOperationException("Remote E2E requires the E2eScenarioToken test parameter.");
            }

            BaseUrl = externalBaseUrl.TrimEnd('/');
            E2eScenarioToken = scenarioToken;
            return;
        }

        Environment.SetEnvironmentVariable("PLANDECK_E2E_TESTAUTH", "true");
        Environment.SetEnvironmentVariable("PLANDECK_E2E_SCENARIO_TOKEN", LocalScenarioToken);

        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.PlanDeck_AppHost>();
        EnsureAzureProvisioningConfigured(builder.Configuration);

        _app = await builder.BuildAsync();
        await _app.StartAsync();

        var notifications = _app.Services.GetRequiredService<ResourceNotificationService>();
        await notifications.WaitForResourceAsync("plandeck-server", KnownResourceStates.Running).WaitAsync(TimeSpan.FromMinutes(5));

        BaseUrl = await ResolveBaseUrlFromAspireClientAsync(_app);
        E2eScenarioToken = LocalScenarioToken;
    }

    private static async Task<string> ResolveBaseUrlFromAspireClientAsync(DistributedApplication app)
    {
        using var client = app.CreateHttpClient("plandeck-server");

        if (client.BaseAddress is not { } baseAddress)
        {
            throw new InvalidOperationException("Aspire HttpClient for 'plandeck-server' has no BaseAddress.");
        }

        var deadline = DateTimeOffset.UtcNow.AddMinutes(5);
        Exception? lastError = null;

        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                using var response = await client.GetAsync("/");
                _ = response.StatusCode;
                return baseAddress.ToString().TrimEnd('/');
            }
            catch (Exception ex)
            {
                lastError = ex;
                await Task.Delay(TimeSpan.FromSeconds(2));
            }
        }

        throw new InvalidOperationException(
            $"Aspire plandeck-server endpoint stayed unreachable at '{baseAddress}'.", lastError);
    }

    private static void EnsureAzureProvisioningConfigured(IConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(configuration["Azure:SubscriptionId"]) || string.IsNullOrWhiteSpace(configuration["Azure:Location"]))
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
