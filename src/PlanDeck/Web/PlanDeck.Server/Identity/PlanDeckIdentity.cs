using System.Globalization;
using System.Security.Claims;
using PlanDeck.Common.Identity;

namespace PlanDeck.Server.Identity;

public static class PlanDeckIdentity
{
    public static bool IsValidMember(ClaimsPrincipal? principal) =>
        principal?.Identity?.IsAuthenticated == true
        && !IsGuest(principal)
        && TryReadGuid(principal, PlanDeckClaimTypes.MemberTenantId, out _)
        && TryReadGuid(principal, PlanDeckClaimTypes.UserId, out _)
        && !string.IsNullOrWhiteSpace(principal.FindFirstValue(PlanDeckClaimTypes.TenantRole))
        && string.Equals(
            principal.FindFirstValue(PlanDeckClaimTypes.ActiveUser),
            bool.TrueString,
            StringComparison.OrdinalIgnoreCase);

    public static bool IsValidGuest(ClaimsPrincipal? principal) =>
        principal?.Identity?.IsAuthenticated == true
        && IsGuest(principal)
        && TryReadGuid(principal, PlanDeckClaimTypes.EntraTenantId, out _)
        && TryReadGuid(principal, PlanDeckClaimTypes.EntraObjectId, out _)
        && TryReadGuid(principal, PlanDeckClaimTypes.SessionId, out _);

    public static bool IsValidRoomIdentity(ClaimsPrincipal? principal) =>
        IsValidMember(principal) || IsValidGuest(principal);

    public static bool IsGuest(ClaimsPrincipal? principal) =>
        string.Equals(
            principal?.FindFirstValue(PlanDeckClaimTypes.IsGuest),
            bool.TrueString,
            StringComparison.OrdinalIgnoreCase);

    public static bool TryReadGuid(ClaimsPrincipal principal, string claimType, out Guid value) =>
        Guid.TryParse(
            principal.FindFirstValue(claimType),
            CultureInfo.InvariantCulture,
            out value)
        && value != Guid.Empty;
}
