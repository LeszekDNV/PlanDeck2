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

        Assert.That(
            () => services.AddExternalServices(
                configuration,
                new ProductionEnvironment()),
            Throws.TypeOf<InvalidOperationException>()
                .With.Message.Contains("Production requires"));
    }

    [Test]
    public void ProductionWithTestAuthentication_FailsClosed()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationManager();
        configuration["Authentication:UseTestScheme"] = bool.TrueString;

        Assert.That(
            () => services.AddExternalServices(
                configuration,
                new ProductionEnvironment()),
            Throws.TypeOf<InvalidOperationException>()
                .With.Message.Contains("only permitted"));
    }

    private sealed class ProductionEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;

        public string ApplicationName { get; set; } = nameof(PlanDeck);

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } =
            new NullFileProvider();
    }
}
