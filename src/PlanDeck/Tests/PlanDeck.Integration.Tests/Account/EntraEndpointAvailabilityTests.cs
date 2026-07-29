using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using PlanDeck.Server;

namespace PlanDeck.Integration.Tests.Account;

[TestFixture]
public sealed class EntraEndpointAvailabilityTests
{
    private static readonly string[] EntraChallengeRoutes =
    [
        "/account/entra/login",
        "/account/entra/register",
        "/account/entra/link"
    ];

    [Test]
    public async Task CompleteMicrosoftConfiguration_MapsSchemeAndChallengeRoutes()
    {
        using var factory = CreateFactory(microsoftAuthenticationAvailable: true);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var routes = GetRoutePatterns(factory);
        var schemeProvider = factory.Services.GetRequiredService<IAuthenticationSchemeProvider>();
        var scheme = await schemeProvider.GetSchemeAsync(OpenIdConnectDefaults.AuthenticationScheme);
        var loginResponse = await client.GetAsync(EntraChallengeRoutes[0]);
        var registerResponse = await client.GetAsync(EntraChallengeRoutes[1]);
        var linkEndpoint = GetRouteEndpoints(factory)
            .Single(endpoint => endpoint.RoutePattern.RawText == EntraChallengeRoutes[2]);

        Assert.Multiple(() =>
        {
            Assert.That(scheme, Is.Not.Null);
            Assert.That(routes, Does.Contain(EntraChallengeRoutes[0]));
            Assert.That(routes, Does.Contain(EntraChallengeRoutes[1]));
            Assert.That(routes, Does.Contain(EntraChallengeRoutes[2]));
            AssertMicrosoftChallenge(loginResponse);
            AssertMicrosoftChallenge(registerResponse);
            Assert.That(linkEndpoint.Metadata.GetMetadata<IAuthorizeData>(), Is.Not.Null);
        });
    }

    [Test]
    public async Task OptionalIncompleteMicrosoftConfiguration_OmitsChallengeRoutesAndReturnsNotFound()
    {
        using var factory = CreateFactory(microsoftAuthenticationAvailable: false);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var routes = GetRoutePatterns(factory);
        var loginResponse = await client.GetAsync(EntraChallengeRoutes[0]);
        var registerResponse = await client.GetAsync(EntraChallengeRoutes[1]);
        var linkResponse = await client.PostAsJsonAsync(
            EntraChallengeRoutes[2],
            new { Password = "unused", ReturnUrl = "/" });

        Assert.Multiple(() =>
        {
            Assert.That(routes, Does.Not.Contain(EntraChallengeRoutes[0]));
            Assert.That(routes, Does.Not.Contain(EntraChallengeRoutes[1]));
            Assert.That(routes, Does.Not.Contain(EntraChallengeRoutes[2]));
            Assert.That(loginResponse.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(registerResponse.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(linkResponse.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        });
    }

    [Test]
    public void OptionalIncompleteMicrosoftConfiguration_KeepsLocalAndUnlinkRoutes()
    {
        using var factory = CreateFactory(microsoftAuthenticationAvailable: false);

        var routes = GetRoutePatterns(factory);

        Assert.Multiple(() =>
        {
            Assert.That(routes, Does.Contain("/account/register"));
            Assert.That(routes, Does.Contain("/account/login"));
            Assert.That(routes, Does.Contain("/account/entra/unlink"));
        });
    }

    private static WebApplicationFactory<ServerEntryPoint> CreateFactory(
        bool microsoftAuthenticationAvailable)
    {
        return new WebApplicationFactory<ServerEntryPoint>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting(
                    "ConnectionStrings:DefaultConnection",
                    AspireAppFixture.ConnectionString);
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

    private static string[] GetRoutePatterns(
        WebApplicationFactory<ServerEntryPoint> factory)
    {
        return GetRouteEndpoints(factory)
            .Select(endpoint => endpoint.RoutePattern.RawText)
            .OfType<string>()
            .ToArray();
    }

    private static RouteEndpoint[] GetRouteEndpoints(
        WebApplicationFactory<ServerEntryPoint> factory)
    {
        return factory.Services
            .GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .ToArray();
    }

    private static void AssertMicrosoftChallenge(HttpResponseMessage response)
    {
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
        Assert.That(response.Headers.Location, Is.Not.Null);
        Assert.That(response.Headers.Location!.Host, Is.EqualTo("login.microsoftonline.com"));
    }
}
