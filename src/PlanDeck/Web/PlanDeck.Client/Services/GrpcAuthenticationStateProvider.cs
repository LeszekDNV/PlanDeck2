using System.Security.Claims;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Components.Authorization;
using PlanDeck.Core.Shared.Contracts;
using ProtoBuf.Grpc.Client;

namespace PlanDeck.Client.Services;

public sealed class GrpcAuthenticationStateProvider(GrpcChannel channel) : AuthenticationStateProvider
{
    private const string AuthenticationType = "PlanDeckGrpc";

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        try
        {
            var service = channel.CreateGrpcService<IAuthService>();
            var reply = await service.GetCurrentUserAsync(new CurrentUserRequest());

            if (!reply.IsAuthenticated)
            {
                return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
            }

            var claims = new List<Claim>();
            if (!string.IsNullOrWhiteSpace(reply.DisplayName))
            {
                claims.Add(new Claim(ClaimTypes.Name, reply.DisplayName));
            }

            if (!string.IsNullOrWhiteSpace(reply.Email))
            {
                claims.Add(new Claim(ClaimTypes.Email, reply.Email));
            }

            var identity = new ClaimsIdentity(claims, AuthenticationType);
            return new AuthenticationState(new ClaimsPrincipal(identity));
        }
        catch
        {
            return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
        }
    }

    public void NotifyAuthenticationStateChanged()
    {
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }
}
