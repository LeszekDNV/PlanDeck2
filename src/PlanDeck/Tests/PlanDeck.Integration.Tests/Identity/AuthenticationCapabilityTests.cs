using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using PlanDeck.Server;

namespace PlanDeck.Identity.IntegrationTests;

[TestFixture]
public sealed class AuthenticationCapabilityTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task CapabilityMatchesMicrosoftAuthenticationConfiguration(
        bool microsoftAuthenticationAvailable)
    {
        using var factory = CreateFactory(microsoftAuthenticationAvailable);
        using var client = new AuthenticationTestClient(factory);

        var capabilities = await client.GetAuthenticationCapabilitiesAsync();
        var currentUser = await client.GetCurrentUserAsync();

        Assert.Multiple(() =>
        {
            Assert.That(
                capabilities.MicrosoftAuthenticationAvailable,
                Is.EqualTo(microsoftAuthenticationAvailable));
            Assert.That(currentUser.IsAuthenticated, Is.False);
        });
    }

    private static WebApplicationFactory<ServerEntryPoint> CreateFactory(
        bool microsoftAuthenticationAvailable)
    {
        return new WebApplicationFactory<ServerEntryPoint>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.UseSetting(
                    "ConnectionStrings:DefaultConnection",
                    "Server=localhost;Database=PlanDeckCapabilityTests;"
                    + "User Id=sa;Password=LocalOnly_123!;TrustServerCertificate=True");
                builder.UseSetting(
                    "Authentication:Microsoft:TenantId",
                    microsoftAuthenticationAvailable ? "tenant-id" : string.Empty);
                builder.UseSetting(
                    "Authentication:Microsoft:ClientId",
                    microsoftAuthenticationAvailable ? "client-id" : string.Empty);
                builder.UseSetting(
                    "Authentication:Microsoft:ClientSecret",
                    microsoftAuthenticationAvailable ? "client-secret" : string.Empty);
                builder.UseSetting("Authentication:Microsoft:Required", bool.FalseString);
                builder.UseSetting("RateLimiting:Disable", bool.TrueString);
            });
    }
}
