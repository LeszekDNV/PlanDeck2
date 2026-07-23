using Microsoft.Playwright;

namespace PlanDeck.E2e.Tests;

public static class E2eIdentityContextFactory
{
    private const string UserSelectionCookie = "e2e-user";
    private const string GuestSessionCookie = "e2e-guest-sid";

    public static Task<IBrowserContext> CreateOwnerContextAsync(
        IBrowser browser,
        string baseUrl,
        BrowserNewContextOptions options,
        string culture = "en") =>
        CreateMemberContextAsync(browser, baseUrl, "owner", options, culture);

    public static Task<IBrowserContext> CreateAdminContextAsync(
        IBrowser browser,
        string baseUrl,
        BrowserNewContextOptions options,
        string culture = "en") =>
        CreateMemberContextAsync(browser, baseUrl, "admin", options, culture);

    public static Task<IBrowserContext> CreateMemberContextAsync(
        IBrowser browser,
        string baseUrl,
        BrowserNewContextOptions options,
        string culture = "en") =>
        CreateMemberContextAsync(browser, baseUrl, "member", options, culture);

    public static async Task<IBrowserContext> CreateGuestContextAsync(
        IBrowser browser,
        string baseUrl,
        Guid sessionId,
        BrowserNewContextOptions options)
    {
        var context = await browser.NewContextAsync(options);
        await context.AddCookiesAsync(
        [
            CreateCookie(baseUrl, GuestSessionCookie, sessionId.ToString())
        ]);
        return context;
    }

    private static async Task<IBrowserContext> CreateMemberContextAsync(
        IBrowser browser,
        string baseUrl,
        string selectionKey,
        BrowserNewContextOptions options,
        string culture)
    {
        var context = await browser.NewContextAsync(options);
        await context.AddInitScriptAsync($"window.localStorage.setItem('BlazorCulture', '{culture}')");
        await context.AddCookiesAsync(
        [
            CreateCookie(baseUrl, UserSelectionCookie, selectionKey)
        ]);
        return context;
    }

    private static Cookie CreateCookie(string baseUrl, string name, string value)
    {
        var secure = string.Equals(new Uri(baseUrl, UriKind.Absolute).Scheme, "https", StringComparison.OrdinalIgnoreCase);
        return new Cookie
        {
            Name = name,
            Value = value,
            Url = baseUrl,
            SameSite = SameSiteAttribute.None,
            Secure = secure
        };
    }
}
