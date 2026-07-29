using PlanDeck.Application.Abstractions;
using PlanDeck.Core.Shared.Contracts;
using ProtoBuf.Grpc;

namespace PlanDeck.Application.Services;

public sealed class AuthGrpcService(
    ICurrentUserContext currentUser,
    IAuthenticationCapabilities authenticationCapabilities) : IAuthService
{
    public Task<CurrentUserReply> GetCurrentUserAsync(CurrentUserRequest request, CallContext context = default)
    {
        return Task.FromResult(new CurrentUserReply
        {
            IsAuthenticated = currentUser.IsAuthenticated,
            DisplayName = currentUser.IsAuthenticated ? currentUser.DisplayName : null,
            Email = currentUser.IsAuthenticated ? currentUser.Email : null,
            ParticipantId = currentUser.IsAuthenticated ? currentUser.ParticipantId : null,
            IsGuest = currentUser.IsAuthenticated && currentUser.IsGuest
        });
    }

    public Task<AuthenticationCapabilitiesReply> GetAuthenticationCapabilitiesAsync(
        AuthenticationCapabilitiesRequest request,
        CallContext context = default)
    {
        return Task.FromResult(new AuthenticationCapabilitiesReply
        {
            MicrosoftAuthenticationAvailable =
                authenticationCapabilities.MicrosoftAuthenticationAvailable
        });
    }
}
