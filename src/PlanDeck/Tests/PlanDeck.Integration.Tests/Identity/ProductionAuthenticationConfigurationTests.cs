using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using PlanDeck.Server.Extensions;

// Outside PlanDeck.Integration.Tests so configuration tests do not boot Aspire.
namespace PlanDeck.Identity.IntegrationTests;

[TestFixture]
public sealed class ProductionAuthenticationConfigurationTests
{
    [Test]
    public void RequiredMicrosoftAuthenticationWithoutCompleteConfiguration_FailsClosed()
    {
        var services = new ServiceCollection();
        var configuration = CreateConfiguration();
        configuration["Authentication:Microsoft:Required"] = bool.TrueString;

        Assert.That(
            () => services.AddExternalServices(
                configuration,
                new TestingEnvironment()),
            Throws.TypeOf<InvalidOperationException>()
                .With.Message.EqualTo(
                    "Microsoft authentication is required. Configure "
                    + "Authentication:Microsoft:TenantId, ClientId, and ClientSecret."));
    }

    [Test]
    public void RequiredMicrosoftAuthenticationWithPartialConfiguration_FailsClosed()
    {
        var services = new ServiceCollection();
        var configuration = CreateConfiguration();
        configuration["Authentication:Microsoft:Required"] = bool.TrueString;
        configuration["Authentication:Microsoft:TenantId"] = "tenant-id";
        configuration["Authentication:Microsoft:ClientId"] = "client-id";

        Assert.That(
            () => services.AddExternalServices(
                configuration,
                new TestingEnvironment()),
            Throws.TypeOf<InvalidOperationException>()
                .With.Message.EqualTo(
                    "Microsoft authentication is required. Configure "
                    + "Authentication:Microsoft:TenantId, ClientId, and ClientSecret."));
    }

    [Test]
    public void RequiredMicrosoftAuthenticationWithCompleteConfiguration_Starts()
    {
        var services = new ServiceCollection();
        var configuration = CreateConfiguration();
        configuration["Authentication:Microsoft:Required"] = bool.TrueString;
        configuration["Authentication:Microsoft:TenantId"] = "tenant-id";
        configuration["Authentication:Microsoft:ClientId"] = "client-id";
        configuration["Authentication:Microsoft:ClientSecret"] = "client-secret";

        Assert.That(
            () => services.AddExternalServices(
                configuration,
                new TestingEnvironment()),
            Throws.Nothing);
    }

    [Test]
    public void OptionalMicrosoftAuthenticationWithoutConfiguration_Starts()
    {
        var services = new ServiceCollection();
        var configuration = CreateConfiguration();

        Assert.That(
            () => services.AddExternalServices(
                configuration,
                new TestingEnvironment()),
            Throws.Nothing);
    }

    private static ConfigurationManager CreateConfiguration() => new();

    private sealed class TestingEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";

        public string ApplicationName { get; set; } = nameof(PlanDeck);

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } =
            new NullFileProvider();
    }
}
