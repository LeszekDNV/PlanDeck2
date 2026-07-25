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
    public void ProductionWithoutCompleteEntraConfiguration_FailsClosed()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationManager();
        configuration["EmailSettings:Host"] = "smtp.example.com";
        configuration["EmailSettings:SenderAddress"] = "noreply@example.com";
        configuration["EmailSettings:PublicBaseUrl"] = "https://example.com";

        Assert.That(
            () => services.AddExternalServices(
                configuration,
                new ProductionEnvironment()),
            Throws.TypeOf<InvalidOperationException>()
                .With.Message.Contains("Production requires"));
    }

    [Test]
    public void ProductionWithLegacyTestAuthenticationFlag_StillFailsWithoutEntraConfiguration()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationManager();
        configuration["Authentication:UseTestScheme"] = bool.TrueString;
        configuration["EmailSettings:Host"] = "smtp.example.com";
        configuration["EmailSettings:SenderAddress"] = "noreply@example.com";
        configuration["EmailSettings:PublicBaseUrl"] = "https://example.com";

        Assert.That(
            () => services.AddExternalServices(
                configuration,
                new ProductionEnvironment()),
            Throws.TypeOf<InvalidOperationException>()
                .With.Message.Contains("Production requires"));
    }

    [Test]
    public void TestingWithoutEntraConfiguration_UsesLocalAccountAuthentication()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationManager();
        configuration["EmailSettings:Host"] = "smtp.example.com";
        configuration["EmailSettings:SenderAddress"] = "noreply@example.com";
        configuration["EmailSettings:PublicBaseUrl"] = "https://example.com";

        Assert.That(
            () => services.AddExternalServices(
                configuration,
                new TestingEnvironment()),
            Throws.Nothing);
    }

    private sealed class ProductionEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;

        public string ApplicationName { get; set; } = nameof(PlanDeck);

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } =
            new NullFileProvider();
    }

    private sealed class TestingEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";

        public string ApplicationName { get; set; } = nameof(PlanDeck);

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } =
            new NullFileProvider();
    }
}
