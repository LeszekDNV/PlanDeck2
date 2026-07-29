using System.Security.Claims;

namespace PlanDeck.Client.Pages;

public enum HomePageView
{
    Loading,
    Anonymous,
    Registered,
    Guest
}

public static class HomePagePolicy
{
    public static HomePageView GetView(ClaimsPrincipal user)
    {
        if (user.Identity?.IsAuthenticated != true)
        {
            return HomePageView.Anonymous;
        }

        return user.Claims.Any(claim =>
            claim.Type == "is_guest" &&
            string.Equals(claim.Value, bool.TrueString, StringComparison.OrdinalIgnoreCase))
                ? HomePageView.Guest
                : HomePageView.Registered;
    }

    public static string? BuildJoinRoute(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        return $"/join/{Uri.EscapeDataString(code.Trim())}";
    }
}
