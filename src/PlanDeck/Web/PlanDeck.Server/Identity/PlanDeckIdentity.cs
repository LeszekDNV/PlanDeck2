using System.Globalization;
using System.Security.Claims;

namespace PlanDeck.Server.Identity;

public static class PlanDeckIdentity
{
    public const string TenantIdClaim = "tid";
    public const string EntraObjectIdClaim = "oid";
    public const string AppUserIdClaim = "plandeck_user_id";
    public const string ActiveUserClaim = "plandeck_user_active";

    public static bool IsValidMember(ClaimsPrincipal? principal) =>
        principal?.Identity?.IsAuthenticated == true
        && !IsGuest(principal)
        && TryReadGuid(principal, TenantIdClaim, out _)
        && TryReadGuid(principal, EntraObjectIdClaim, out _)
        && TryReadGuid(principal, AppUserIdClaim, out _)
        && string.Equals(
            principal.FindFirstValue(ActiveUserClaim),
            bool.TrueString,
            StringComparison.OrdinalIgnoreCase);

    public static bool IsValidGuest(ClaimsPrincipal? principal) =>
        principal?.Identity?.IsAuthenticated == true
        && IsGuest(principal)
        && TryReadGuid(principal, TenantIdClaim, out _)
        && TryReadGuid(principal, EntraObjectIdClaim, out _)
        && TryReadGuid(principal, GuestAuthentication.SessionIdClaim, out _);

    public static bool IsValidRoomIdentity(ClaimsPrincipal? principal) =>
        IsValidMember(principal) || IsValidGuest(principal);

    public static bool IsGuest(ClaimsPrincipal principal) =>
        string.Equals(
            principal.FindFirstValue(GuestAuthentication.IsGuestClaim),
            bool.TrueString,
            StringComparison.OrdinalIgnoreCase);

    public static bool TryReadGuid(ClaimsPrincipal principal, string claimType, out Guid value) =>
        Guid.TryParse(
            principal.FindFirstValue(claimType),
            CultureInfo.InvariantCulture,
            out value)
        && value != Guid.Empty;
}
