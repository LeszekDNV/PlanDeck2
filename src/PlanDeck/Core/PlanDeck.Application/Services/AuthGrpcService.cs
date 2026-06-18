using PlanDeck.Application.Abstractions;
using PlanDeck.Core.Shared.Contracts;
using ProtoBuf.Grpc;

namespace PlanDeck.Application.Services;

public sealed class AuthGrpcService(ICurrentUserContext currentUser) : IAuthService
{
    public Task<CurrentUserReply> GetCurrentUserAsync(CurrentUserRequest request, CallContext context = default)
    {
        return Task.FromResult(new CurrentUserReply
        {
            IsAuthenticated = currentUser.IsAuthenticated,
            DisplayName = currentUser.IsAuthenticated ? currentUser.DisplayName : null,
            Email = currentUser.IsAuthenticated ? currentUser.Email : null
        });
    }
}
