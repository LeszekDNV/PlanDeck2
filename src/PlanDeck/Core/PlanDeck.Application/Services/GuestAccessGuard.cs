using Grpc.Core;
using PlanDeck.Application.Abstractions;

namespace PlanDeck.Application.Services;

/// <summary>
/// Guards the management gRPC surface against account-less guests. A guest cookie carries the host
/// tenant (so tenant-scoped reads resolve), but a guest is only authorised to vote in the single
/// session it joined — it must never reach session/team/member administration of the tenant.
/// </summary>
internal static class GuestAccessGuard
{
    public static void RejectGuests(ICurrentUserContext currentUser)
    {
        if (!currentUser.IsAuthenticated)
        {
            throw new RpcException(new Status(
                StatusCode.Unauthenticated,
                "Authentication is required."));
        }

        if (currentUser.IsGuest)
        {
            throw new RpcException(new Status(
                StatusCode.PermissionDenied,
                "Guests are not permitted to perform this operation."));
        }
    }
}
