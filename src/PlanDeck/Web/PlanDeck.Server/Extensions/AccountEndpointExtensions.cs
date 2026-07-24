using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using PlanDeck.Application.Abstractions;
using PlanDeck.Application.Account;
using PlanDeck.Common.Identity;
using PlanDeck.Infrastructure.Identity;
using PlanDeck.Server.Identity;

namespace PlanDeck.Server.Extensions;

public static class AccountEndpointExtensions
{
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/account/register", async (
            LocalRegisterRequest request,
            ILocalAccountService accountService,
            CancellationToken cancellationToken) =>
        {
            var result = await accountService.RegisterAsync(request, cancellationToken);

            return result.Succeeded
                ? Results.Ok(new AccountResponse(result.Status.ToString(), result.UserId))
                : Results.BadRequest(new AccountResponse(result.Status.ToString(), result.UserId, result.Errors));
        })
        .AllowAnonymous()
        .RequireRateLimiting("register")
        .WithName("AccountRegister")
        .WithDisplayName("Account Register");

        app.MapPost("/account/login", async (
            LocalLoginRequest request,
            string? returnUrl,
            SignInManager<ApplicationUser> signInManager,
            UserManager<ApplicationUser> userManager,
            ILookupNormalizer lookupNormalizer,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var normalizedIdentifier = NormalizeLoginIdentifier(request.Login, lookupNormalizer);
            var user = await FindUserByNormalizedIdentifierAsync(
                userManager,
                request.Login,
                normalizedIdentifier,
                cancellationToken);

            if (user is null)
            {
                return MapLoginFailure(LocalLoginStatus.InvalidCredentials, returnUrl, httpContext.Request);
            }

            var checkResult = await signInManager.CheckPasswordSignInAsync(
                user,
                request.Password,
                lockoutOnFailure: true);

            if (checkResult.Succeeded)
            {
                if (!user.EmailConfirmed)
                {
                    return MapLoginFailure(LocalLoginStatus.InvalidCredentials, returnUrl, httpContext.Request);
                }

                var principal = await signInManager.CreateUserPrincipalAsync(user);
                await httpContext.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    principal,
                    new AuthenticationProperties { IsPersistent = request.RememberMe });

                return Results.Ok(new AccountResponse(
                    LocalLoginStatus.Success.ToString(),
                    user.Id,
                    null,
                    ResolveLocalReturnUrl(httpContext.Request, returnUrl)));
            }

            var status = checkResult.IsLockedOut
                ? LocalLoginStatus.LockedOut
                : LocalLoginStatus.InvalidCredentials;

            return MapLoginFailure(status, returnUrl, httpContext.Request);
        })
        .AllowAnonymous()
        .RequireRateLimiting("login")
        .WithName("AccountLogin")
        .WithDisplayName("Account Login");

        app.MapGet("/account/confirm-email", async (
            Guid userId,
            string token,
            IAccountLifecycleService lifecycleService,
            CancellationToken cancellationToken) =>
        {
            var result = await lifecycleService.ConfirmEmailAsync(userId, token, cancellationToken);

            return result.Status switch
            {
                ConfirmEmailStatus.Success => Results.Ok(new AccountResponse(result.Status.ToString())),
                ConfirmEmailStatus.AlreadyConfirmed => Results.Ok(new AccountResponse(result.Status.ToString())),
                _ => Results.BadRequest(new AccountResponse(result.Status.ToString(), null, result.Errors))
            };
        })
        .AllowAnonymous()
        .WithName("AccountConfirmEmail")
        .WithDisplayName("Account Confirm Email");

        app.MapPost("/account/resend-confirmation", async (
            ResendConfirmationRequest request,
            IAccountLifecycleService lifecycleService,
            CancellationToken cancellationToken) =>
        {
            var result = await lifecycleService.ResendConfirmationAsync(request.Email, cancellationToken);
            return Results.Ok(new AccountResponse(result.Status.ToString(), null, result.Errors));
        })
        .AllowAnonymous()
        .RequireRateLimiting("resend-confirmation")
        .WithName("AccountResendConfirmation")
        .WithDisplayName("Account Resend Confirmation");

        app.MapPost("/account/forgot-password", async (
            ForgotPasswordRequest request,
            IAccountLifecycleService lifecycleService,
            CancellationToken cancellationToken) =>
        {
            var result = await lifecycleService.SendPasswordResetAsync(request.Email, cancellationToken);
            return Results.Ok(new AccountResponse(result.Status.ToString(), null, result.Errors));
        })
        .AllowAnonymous()
        .RequireRateLimiting("forgot-password")
        .WithName("AccountForgotPassword")
        .WithDisplayName("Account Forgot Password");

        app.MapPost("/account/reset-password", async (
            ResetPasswordRequest request,
            IAccountLifecycleService lifecycleService,
            CancellationToken cancellationToken) =>
        {
            var result = await lifecycleService.ResetPasswordAsync(
                request.Email,
                request.Token,
                request.NewPassword,
                cancellationToken);

            return result.Status == ResetPasswordStatus.Success
                ? Results.Ok(new AccountResponse(result.Status.ToString()))
                : Results.BadRequest(new AccountResponse(result.Status.ToString(), null, result.Errors));
        })
        .AllowAnonymous()
        .RequireRateLimiting("forgot-password")
        .WithName("AccountResetPassword")
        .WithDisplayName("Account Reset Password");

        app.MapGet("/account/antiforgery", async (
            IAntiforgery antiforgery,
            HttpContext httpContext) =>
        {
            var tokens = antiforgery.GetAndStoreTokens(httpContext);
            return Results.Ok(new { Token = tokens.RequestToken });
        })
        .AllowAnonymous()
        .WithName("AccountAntiforgery")
        .WithDisplayName("Account Antiforgery");

        app.MapPost("/account/logout", async (
            IAntiforgery antiforgery,
            HttpContext httpContext) =>
        {
            if (!await antiforgery.IsRequestValidAsync(httpContext))
            {
                return Results.BadRequest(new AccountResponse("InvalidAntiForgeryToken", null, ["Invalid antiforgery token."]));
            }

            await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Ok(new AccountResponse("Success", null, null, "/"));
        })
        .RequireAuthorization(PlanDeckPolicies.MemberAccount)
        .RequireRateLimiting("login")
        .WithName("AccountLogout")
        .WithDisplayName("Account Logout");

        app.MapGet("/account/entra/login", (
            string? returnUrl,
            HttpContext httpContext) =>
        {
            var target = ResolveLocalReturnUrl(httpContext.Request, returnUrl);
            var properties = EntraCallbackHandler.CreateChallengeProperties("login", target);
            return Results.Challenge(properties, [OpenIdConnectDefaults.AuthenticationScheme]);
        })
        .AllowAnonymous()
        .WithName("AccountEntraLogin")
        .WithDisplayName("Account Entra Login");

        app.MapGet("/account/entra/register", (
            string? returnUrl,
            string? invitationToken,
            HttpContext httpContext) =>
        {
            var target = ResolveLocalReturnUrl(httpContext.Request, returnUrl);
            var properties = EntraCallbackHandler.CreateChallengeProperties("register", target, invitationToken);
            return Results.Challenge(properties, [OpenIdConnectDefaults.AuthenticationScheme]);
        })
        .AllowAnonymous()
        .WithName("AccountEntraRegister")
        .WithDisplayName("Account Entra Register");

        app.MapPost("/account/entra/link", async (
            LinkEntraRequest request,
            HttpContext httpContext,
            IAntiforgery antiforgery,
            UserManager<ApplicationUser> userManager) =>
        {
            if (!await antiforgery.IsRequestValidAsync(httpContext))
            {
                return Results.BadRequest(new AccountResponse("InvalidAntiForgeryToken", null, ["Invalid antiforgery token."]));
            }

            var userId = ReadCurrentUserId(httpContext.User);
            if (userId is null)
            {
                return Results.Unauthorized();
            }

            var user = await userManager.FindByIdAsync(userId.Value.ToString());
            if (user is null)
            {
                return Results.Unauthorized();
            }

            if (!await userManager.HasPasswordAsync(user))
            {
                return Results.BadRequest(new AccountResponse("NoLocalPassword", null, ["Add a local password before linking a Microsoft identity."]));
            }

            if (!await userManager.CheckPasswordAsync(user, request.Password))
            {
                return Results.BadRequest(new AccountResponse("InvalidPassword", null, ["Invalid password."]));
            }

            var target = ResolveLocalReturnUrl(httpContext.Request, request.ReturnUrl);
            var properties = EntraCallbackHandler.CreateChallengeProperties("link", target, linkUserId: userId.Value);
            return Results.Challenge(properties, [OpenIdConnectDefaults.AuthenticationScheme]);
        })
        .RequireAuthorization(PlanDeckPolicies.MemberAccount)
        .RequireRateLimiting("login")
        .WithName("AccountEntraLink")
        .WithDisplayName("Account Entra Link");

        app.MapPost("/account/entra/unlink", async (
            UnlinkEntraRequest request,
            HttpContext httpContext,
            IAntiforgery antiforgery,
            IExternalAccountService externalAccountService,
            CancellationToken cancellationToken) =>
        {
            if (!await antiforgery.IsRequestValidAsync(httpContext))
            {
                return Results.BadRequest(new AccountResponse("InvalidAntiForgeryToken", null, ["Invalid antiforgery token."]));
            }

            var userId = ReadCurrentUserId(httpContext.User);
            if (userId is null)
            {
                return Results.Unauthorized();
            }

            var result = await externalAccountService.UnlinkAsync(
                userId.Value,
                request.Provider,
                request.ProviderKey,
                cancellationToken);

            return result.Succeeded
                ? Results.Ok(new AccountResponse(result.Status.ToString()))
                : Results.BadRequest(new AccountResponse(result.Status.ToString(), null, result.Errors));
        })
        .RequireAuthorization(PlanDeckPolicies.MemberAccount)
        .RequireRateLimiting("login")
        .WithName("AccountEntraUnlink")
        .WithDisplayName("Account Entra Unlink");

        return app;
    }

    private static IResult MapLoginFailure(
        LocalLoginStatus status,
        string? returnUrl,
        HttpRequest request)
    {
        var response = new AccountResponse(
            status.ToString(),
            null,
            ["Invalid credentials. Please check your login and password and try again."],
            ResolveLocalReturnUrl(request, returnUrl));

        return status == LocalLoginStatus.LockedOut
            ? Results.StatusCode(StatusCodes.Status429TooManyRequests)
            : Results.BadRequest(response);
    }

    private static string NormalizeLoginIdentifier(string login, ILookupNormalizer normalizer)
    {
        var trimmed = (login ?? string.Empty).Trim();
        return trimmed.Contains('@')
            ? normalizer.NormalizeEmail(trimmed)
            : normalizer.NormalizeName(trimmed);
    }

    private static async Task<ApplicationUser?> FindUserByNormalizedIdentifierAsync(
        UserManager<ApplicationUser> userManager,
        string login,
        string normalizedIdentifier,
        CancellationToken cancellationToken)
    {
        if (login.Contains('@'))
        {
            return await userManager.FindByEmailAsync(normalizedIdentifier)
                .WaitAsync(cancellationToken);
        }

        return await userManager.FindByNameAsync(normalizedIdentifier)
            .WaitAsync(cancellationToken);
    }

    private static Guid? ReadCurrentUserId(ClaimsPrincipal user)
    {
        var claim = user.FindFirst(PlanDeckClaimTypes.UserId);
        if (claim is null || !Guid.TryParse(claim.Value, out var userId) || userId == Guid.Empty)
        {
            return null;
        }

        return userId;
    }

    private static string ResolveLocalReturnUrl(HttpRequest request, string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl))
        {
            return "/";
        }

        if (returnUrl[0] == '/'
            && (returnUrl.Length == 1 || (returnUrl[1] != '/' && returnUrl[1] != '\\')))
        {
            return returnUrl;
        }

        if (Uri.TryCreate(returnUrl, UriKind.Absolute, out var absoluteReturnUrl)
            && string.Equals(absoluteReturnUrl.Scheme, request.Scheme, StringComparison.OrdinalIgnoreCase)
            && absoluteReturnUrl.Authority.Equals(request.Host.Value, StringComparison.OrdinalIgnoreCase))
        {
            return $"{absoluteReturnUrl.PathAndQuery}{absoluteReturnUrl.Fragment}";
        }

        return "/";
    }

    private sealed record AccountResponse(
        string Status,
        Guid? UserId = null,
        IReadOnlyList<string>? Errors = null,
        string? ReturnUrl = null);
}
