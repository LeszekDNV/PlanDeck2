using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Identity;
using PlanDeck.Application.Abstractions;
using PlanDeck.Application.Account;
using PlanDeck.Infrastructure.Identity;
using ExternalLoginInfo = PlanDeck.Application.Account.ExternalLoginInfo;

namespace PlanDeck.Server.Identity;

public sealed class EntraCallbackHandler(
    IExternalAccountService externalAccountService,
    IAccountLifecycleService lifecycleService,
    SignInManager<ApplicationUser> signInManager,
    UserManager<ApplicationUser> userManager,
    ILogger<EntraCallbackHandler> logger)
{
    private const string MicrosoftEntraProvider = "MicrosoftEntra";
    private const string IntentKey = "plandeck_oidc_intent";
    private const string ReturnUrlKey = "plandeck_return_url";
    private const string InvitationTokenKey = "plandeck_invitation_token";
    private const string LinkUserIdKey = "plandeck_link_user_id";

    public Task OnRedirectToIdentityProviderAsync(RedirectContext context)
    {
        if (context.Properties.Items.TryGetValue(IntentKey, out var intent))
        {
            context.ProtocolMessage.SetParameter("plandeck_intent", intent);
        }

        return Task.CompletedTask;
    }

    public async Task OnTokenValidatedAsync(TokenValidatedContext context)
    {
        var principal = context.Principal;
        if (principal?.Identity?.IsAuthenticated != true)
        {
            logger.LogWarning("External identity was not authenticated.");
            context.Fail("External identity was not authenticated.");
            return;
        }

        var loginInfo = ReadExternalLoginInfo(principal);
        if (string.IsNullOrWhiteSpace(loginInfo.ProviderKey))
        {
            logger.LogWarning("External identity did not provide a stable provider key.");
            context.Fail("External identity did not provide a stable provider key.");
            return;
        }

        if (context.Properties is null)
        {
            logger.LogWarning("External authentication properties are missing.");
            context.Fail("External authentication properties are missing.");
            return;
        }

        var intent = GetProperty(context.Properties, IntentKey) ?? "login";
        var returnUrl = GetProperty(context.Properties, ReturnUrlKey) ?? "/";
        var invitationToken = GetProperty(context.Properties, InvitationTokenKey);

        EntraCallbackStatus status;
        IReadOnlyList<string>? errors = null;
        ApplicationUser? applicationUser = null;

        switch (intent)
        {
            case "login":
                var loginResult = await externalAccountService.LoginAsync(loginInfo, context.HttpContext.RequestAborted);
                status = loginResult.Status;
                errors = loginResult.Errors;
                if (loginResult.Succeeded)
                {
                    applicationUser = await userManager.FindByIdAsync(loginResult.UserId!.Value.ToString());
                }

                break;

            case "register":
                var registerResult = await externalAccountService.RegisterAsync(
                    loginInfo,
                    invitationToken,
                    context.HttpContext.RequestAborted);
                status = registerResult.Status;
                errors = registerResult.Errors;
                if (registerResult.Succeeded)
                {
                    applicationUser = await userManager.FindByIdAsync(registerResult.UserId!.Value.ToString());
                    if (applicationUser is not null)
                    {
                        await lifecycleService.ResendConfirmationAsync(applicationUser.Email ?? string.Empty, context.HttpContext.RequestAborted);
                    }
                }

                break;

            case "link":
                if (!context.Properties.Items.TryGetValue(LinkUserIdKey, out var linkUserIdValue)
                    || !Guid.TryParse(linkUserIdValue, out var linkUserId))
                {
                    context.Fail("Link state is missing or invalid.");
                    return;
                }

                var linkResult = await externalAccountService.LinkAsync(linkUserId, loginInfo, context.HttpContext.RequestAborted);
                status = linkResult.Status;
                errors = linkResult.Errors;
                if (linkResult.Succeeded)
                {
                    applicationUser = await userManager.FindByIdAsync(linkUserId.ToString());
                }

                break;

            default:
                context.Fail($"Unknown external authentication intent: {intent}.");
                return;
        }

        if (applicationUser is null || !status.Equals(EntraCallbackStatus.Success))
        {
            logger.LogWarning(
                "External authentication intent {Intent} failed with status {Status}.",
                intent,
                status);
            var code = status.ToString();
            var errorUrl = $"/account/entra/error?code={Uri.EscapeDataString(code)}";
            if (!string.IsNullOrWhiteSpace(returnUrl) && returnUrl.StartsWith('/'))
            {
                errorUrl += $"&returnUrl={Uri.EscapeDataString(returnUrl)}";
            }

            context.HandleResponse();
            context.Response.Redirect(errorUrl);
            return;
        }

        var memberPrincipal = await signInManager.CreateUserPrincipalAsync(applicationUser);
        context.Principal = memberPrincipal;

        if (context.Properties.Items.ContainsKey(ReturnUrlKey))
        {
            context.Properties.RedirectUri = returnUrl;
        }
    }

    public static AuthenticationProperties CreateChallengeProperties(
        string intent,
        string returnUrl,
        string? invitationToken = null,
        Guid? linkUserId = null)
    {
        var properties = new AuthenticationProperties
        {
            RedirectUri = "/account/entra/callback",
            Items =
            {
                [IntentKey] = intent,
                [ReturnUrlKey] = returnUrl
            }
        };

        if (!string.IsNullOrWhiteSpace(invitationToken))
        {
            properties.Items[InvitationTokenKey] = invitationToken;
        }

        if (linkUserId.HasValue && linkUserId.Value != Guid.Empty)
        {
            properties.Items[LinkUserIdKey] = linkUserId.Value.ToString();
        }

        return properties;
    }

    private static string? GetProperty(AuthenticationProperties properties, string key) =>
        properties.Items.TryGetValue(key, out var value) ? value : null;

    private static ExternalLoginInfo ReadExternalLoginInfo(ClaimsPrincipal principal)
    {
        var tid = principal.FindFirstValue("tid") ?? principal.FindFirstValue("http://schemas.microsoft.com/identity/claims/tenantid");
        var oid = principal.FindFirstValue("oid") ?? principal.FindFirstValue("http://schemas.microsoft.com/identity/claims/objectidentifier");
        var email = principal.FindFirstValue("email") ?? principal.FindFirstValue("preferred_username");
        var firstName = principal.FindFirstValue("given_name");
        var lastName = principal.FindFirstValue("family_name");

        var providerKey = !string.IsNullOrWhiteSpace(tid) && !string.IsNullOrWhiteSpace(oid)
            ? $"{tid}:{oid}"
            : string.Empty;

        return new ExternalLoginInfo(MicrosoftEntraProvider, providerKey, email, firstName, lastName);
    }
}
