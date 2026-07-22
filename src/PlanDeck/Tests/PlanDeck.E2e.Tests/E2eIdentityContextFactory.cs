using Microsoft.Playwright;

namespace PlanDeck.E2e.Tests;

public static class E2eIdentityContextFactory
{
    private const string UserSelectionCookie = "e2e-user";
    private const string GuestSessionCookie = "e2e-guest-sid";

    public static Task<IBrowserContext> CreateOwnerContextAsync(
        IBrowser browser,
        string baseUrl,
        BrowserNewContextOptions options) =>
        CreateMemberContextAsync(browser, baseUrl, "owner", options);

    public static Task<IBrowserContext> CreateAdminContextAsync(
        IBrowser browser,
        string baseUrl,
        BrowserNewContextOptions options) =>
        CreateMemberContextAsync(browser, baseUrl, "admin", options);

    public static Task<IBrowserContext> CreateMemberContextAsync(
        IBrowser browser,
        string baseUrl,
        BrowserNewContextOptions options) =>
        CreateMemberContextAsync(browser, baseUrl, "member", options);

    public static async Task<IBrowserContext> CreateGuestContextAsync(
        IBrowser browser,
        string baseUrl,
        Guid sessionId,
        BrowserNewContextOptions options)
    {
        var context = await browser.NewContextAsync(options);
        await context.AddCookiesAsync(
        [
            new Cookie
            {
                Name = GuestSessionCookie,
                Value = sessionId.ToString(),
                Url = baseUrl
            }
        ]);
        return context;
    }

    private static async Task<IBrowserContext> CreateMemberContextAsync(
        IBrowser browser,
        string baseUrl,
        string selectionKey,
        BrowserNewContextOptions options)
    {
        var context = await browser.NewContextAsync(options);
        await context.AddCookiesAsync(
        [
            new Cookie
            {
                Name = UserSelectionCookie,
                Value = selectionKey,
                Url = baseUrl
            }
        ]);
        return context;
    }
}
