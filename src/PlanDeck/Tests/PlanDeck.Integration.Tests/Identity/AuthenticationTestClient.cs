using System.Net;
using System.Net.Http.Json;
using Grpc.Net.Client;
using Grpc.Net.Client.Web;
using Microsoft.AspNetCore.Mvc.Testing;
using PlanDeck.Core.Shared.Contracts;
using PlanDeck.Server;
using ProtoBuf.Grpc.Client;

namespace PlanDeck.Identity.IntegrationTests;

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
