using Microsoft.Playwright;
using PlanDeck.E2e.Tests.Pages;

namespace PlanDeck.E2e.Tests;

public static class LocalAccountFlow
{
    public static async Task<LocalAccountCredentials> RegisterConfirmAndLoginAsync(IPage page, string baseUrl)
    {
        var credentials = LocalAccountCredentials.Create();

        var register = new RegisterPage(page, baseUrl);
        await register.GotoAsync();

        var registerResponseTask = page.WaitForResponseAsync(response =>
            response.Url.Contains("/account/register", StringComparison.OrdinalIgnoreCase)
            && response.Request.Method.Equals("POST", StringComparison.OrdinalIgnoreCase));

        await register.RegisterAsync(
            credentials.Email,
            credentials.UserName,
            credentials.FirstName,
            credentials.LastName,
            credentials.Password);

        var registerResponse = await registerResponseTask;
        var registerResponseBody = await registerResponse.TextAsync();

        if (!registerResponse.Ok)
        {
            throw new InvalidOperationException(
                $"Registration failed with HTTP {(int)registerResponse.Status}: {registerResponseBody}");
        }

        if (!registerResponseBody.Contains("\"status\":\"Success\"", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Registration returned unexpected payload: {registerResponseBody}");
        }

        await page.WaitForFunctionAsync(
            "expectedPath => window.location.pathname.includes(expectedPath)",
            "/account/confirm-email",
            new() { Timeout = 30_000 });

        await LoginAsync(page, baseUrl, credentials);

        return credentials;
    }

    public static async Task LoginAsync(IPage page, string baseUrl, LocalAccountCredentials credentials)
    {
        var login = new LoginPage(page, baseUrl);
        await login.GotoAsync(returnUrl: "/projects", headingText: "Sign in to PlanDeck");
        await login.LoginAsync(credentials.Email, credentials.Password);

        await page.WaitForFunctionAsync(
            "expectedPath => window.location.pathname === expectedPath",
            "/projects",
            new() { Timeout = 30_000 });
    }
}

public sealed record LocalAccountCredentials(
    string Email,
    string UserName,
    string FirstName,
    string LastName,
    string Password)
{
    public static LocalAccountCredentials Create()
    {
        var suffix = Guid.NewGuid().ToString("N");
        return new LocalAccountCredentials(
            $"e2e-{suffix}@plandeck.local",
            $"e2e_{suffix[..12]}",
            "E2E",
            "User",
            "Str0ng!Passw0rd!");
    }
}
