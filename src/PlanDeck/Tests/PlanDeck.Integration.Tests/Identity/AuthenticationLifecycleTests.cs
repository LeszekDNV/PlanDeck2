using System.Net;
using System.Net.Http.Json;
using Grpc.Net.Client;
using Grpc.Net.Client.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PlanDeck.Core.Shared.Contracts;
using PlanDeck.Infrastructure.Persistence;
using PlanDeck.Server;
using PlanDeck.Server.Identity;
using ProtoBuf.Grpc.Client;

namespace PlanDeck.Identity.IntegrationTests;

[TestFixture]
public sealed class AuthenticationLifecycleTests
{
    private WebApplicationFactory<ServerEntryPoint> _factory = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _factory = new WebApplicationFactory<ServerEntryPoint>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Authentication:UseTestScheme", "true");
            builder.UseSetting(
                "ConnectionStrings:DefaultConnection",
                "Server=(localdb)\\MSSQLLocalDB;Database=PlanDeckAuthenticationLifecycleTest;Trusted_Connection=True;");
            builder.ConfigureServices(services =>
            {
                var descriptors = services
                    .Where(descriptor =>
                        descriptor.ServiceType == typeof(DbContextOptions<PlanDeckDbContext>)
                        || descriptor.ServiceType == typeof(DbContextOptions)
                        || descriptor.ServiceType == typeof(PlanDeckDbContext)
                        || (descriptor.ServiceType.IsGenericType
                            && descriptor.ServiceType.GetGenericTypeDefinition().Name
                                == "IDbContextOptionsConfiguration`1"))
                    .ToList();

                foreach (var descriptor in descriptors)
                {
                    services.Remove(descriptor);
                }

                services.AddDbContext<PlanDeckDbContext>(options =>
                    options.UseInMemoryDatabase($"AuthenticationLifecycle-{Guid.NewGuid():N}"));
            });
        });
    }

    [OneTimeTearDown]
    public void OneTimeTearDown() => _factory.Dispose();

    [Test]
    public async Task TestingLogout_RemainsAnonymousUntilLoginRestoresTestOwner()
    {
        using var browser = new AuthenticationTestClient(_factory);

        var initialUser = await browser.GetCurrentUserAsync();
        Assert.Multiple(() =>
        {
            Assert.That(initialUser.IsAuthenticated, Is.True);
            Assert.That(initialUser.DisplayName, Is.EqualTo("Test Owner"));
        });

        var logoutResponse = await browser.GetAsync("/auth/logout");
        Assert.Multiple(() =>
        {
            Assert.That(logoutResponse.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
            Assert.That(logoutResponse.Headers.Location?.OriginalString, Is.EqualTo("/"));
        });

        var anonymousUser = await browser.GetCurrentUserAsync();
        var refreshedUser = await browser.GetCurrentUserAsync();
        Assert.Multiple(() =>
        {
            Assert.That(anonymousUser.IsAuthenticated, Is.False);
            Assert.That(refreshedUser.IsAuthenticated, Is.False);
        });

        var loginResponse = await browser.GetAsync(
            $"/auth/login?returnUrl={Uri.EscapeDataString($"{browser.BaseAddress}projects")}");
        Assert.Multiple(() =>
        {
            Assert.That(loginResponse.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
            Assert.That(loginResponse.Headers.Location?.OriginalString, Is.EqualTo("/projects"));
        });

        var restoredUser = await browser.GetCurrentUserAsync();
        Assert.Multiple(() =>
        {
            Assert.That(restoredUser.IsAuthenticated, Is.True);
            Assert.That(restoredUser.DisplayName, Is.EqualTo("Test Owner"));
        });
    }

    [Test]
    public async Task TestingLogout_ClearsDeterministicGuestSelector()
    {
        using var browser = new AuthenticationTestClient(_factory);
        browser.SetCookie(TestAuthenticationHandler.GuestSessionCookie, Guid.NewGuid().ToString());

        var guestUser = await browser.GetCurrentUserAsync();
        Assert.That(guestUser.IsGuest, Is.True);

        var logoutResponse = await browser.GetAsync("/auth/logout");

        Assert.Multiple(() =>
        {
            Assert.That(logoutResponse.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
            Assert.That(
                browser.HasCookie(TestAuthenticationHandler.GuestSessionCookie),
                Is.False);
        });
        Assert.That((await browser.GetCurrentUserAsync()).IsAuthenticated, Is.False);
    }

    [Test]
    public async Task TestingLogin_RejectsExternalReturnUrl()
    {
        using var browser = new AuthenticationTestClient(_factory);

        var response = await browser.GetAsync(
            "/auth/login?returnUrl=https%3A%2F%2Fexample.com%2Fexternal");

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
            Assert.That(response.Headers.Location?.OriginalString, Is.EqualTo("/"));
        });
    }
}

internal sealed class AuthenticationTestClient : IDisposable
{
    private readonly CookieContainer _cookies = new();
    private readonly HttpClient _httpClient;
    private readonly GrpcChannel _channel;

    public AuthenticationTestClient(WebApplicationFactory<ServerEntryPoint> factory)
    {
        BaseAddress = new Uri("https://localhost");
        var handler = new BrowserCookieHandler(_cookies)
        {
            InnerHandler = new GrpcWebHandler(
                GrpcWebMode.GrpcWeb,
                factory.Server.CreateHandler())
        };
        _httpClient = new HttpClient(handler) { BaseAddress = BaseAddress };
        _channel = GrpcChannel.ForAddress(
            BaseAddress,
            new GrpcChannelOptions { HttpClient = _httpClient });
    }

    public Uri BaseAddress { get; }

    public Task<HttpResponseMessage> GetAsync(string requestUri) =>
        _httpClient.GetAsync(requestUri);

    public Task<HttpResponseMessage> PostAsJsonAsync<T>(string requestUri, T value) =>
        _httpClient.PostAsJsonAsync(requestUri, value);

    public async Task<CurrentUserReply> GetCurrentUserAsync()
    {
        var service = _channel.CreateGrpcService<IAuthService>();
        return await service.GetCurrentUserAsync(new CurrentUserRequest());
    }

    public void SetCookie(string name, string value) =>
        _cookies.Add(BaseAddress, new Cookie(name, value, "/"));

    public bool HasCookie(string name) =>
        _cookies.GetCookies(BaseAddress).Cast<Cookie>().Any(cookie => cookie.Name == name);

    public void Dispose()
    {
        _channel.Dispose();
        _httpClient.Dispose();
    }

    private sealed class BrowserCookieHandler(CookieContainer cookies) : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var requestUri = request.RequestUri
                ?? throw new InvalidOperationException("The request URI is required.");
            var cookieHeader = cookies.GetCookieHeader(requestUri);
            if (!string.IsNullOrEmpty(cookieHeader))
            {
                request.Headers.TryAddWithoutValidation("Cookie", cookieHeader);
            }

            var response = await base.SendAsync(request, cancellationToken);
            if (response.Headers.TryGetValues("Set-Cookie", out var setCookies))
            {
                foreach (var setCookie in setCookies)
                {
                    cookies.SetCookies(requestUri, setCookie);
                }
            }

            return response;
        }
    }
}
