using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace PlanDeck.E2e.Tests;

[SetUpFixture]
public class AspireAppFixture
{
    private DistributedApplication? _app;

    public static string BaseUrl { get; private set; } = string.Empty;
    public static string MailpitBaseUrl { get; private set; } = string.Empty;

    [OneTimeSetUp]
    public async Task StartAsync()
    {
        Environment.SetEnvironmentVariable("Testing__E2e__AutoConfirmEmail", "true");
        Environment.SetEnvironmentVariable("Testing__E2e__EnableMicrosoftAuthentication", "true");

        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.PlanDeck_AppHost>();
        EnsureAzureProvisioningConfigured(builder.Configuration);

        _app = await builder.BuildAsync();
        await _app.StartAsync();

        var notifications = _app.Services.GetRequiredService<ResourceNotificationService>();
        await notifications.WaitForResourceAsync("plandeck-server", KnownResourceStates.Running).WaitAsync(TimeSpan.FromMinutes(5));

        BaseUrl = await ResolveBaseUrlFromAspireClientAsync(_app);
        MailpitBaseUrl = ResolveBaseUrlFromAspireClient(_app, "smtp");
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

    private static string ResolveBaseUrlFromAspireClient(DistributedApplication app, string resourceName)
    {
        using var client = app.CreateHttpClient(resourceName);
        if (client.BaseAddress is not { } baseAddress)
        {
            throw new InvalidOperationException($"Aspire HttpClient for '{resourceName}' has no BaseAddress.");
        }

        return baseAddress.ToString().TrimEnd('/');
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


