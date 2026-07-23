using PlanDeck.Server.Extensions;
using PlanDeck.Application.Abstractions;
using PlanDeck.Application.Services;
using PlanDeck.Server.Hubs;
using PlanDeck.Server.Identity;
using PlanDeck.Server.Testing;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using ProtoBuf.Grpc.Server;
using System.Globalization;

var builder = WebApplication.CreateBuilder(args);

var useTestSchemeBootstrap = builder.Configuration.GetValue<bool>("Authentication:UseTestScheme");

builder.AddServiceDefaults();
if (!useTestSchemeBootstrap)
{
    builder.AddAzureKeyVaultClient("key-vault");
}

// Add services to the container.
builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");
builder.Services.AddGrpc();
builder.Services.AddSignalR();

builder.Services
    .AddSqlDatabase(builder.Configuration)
    .AddLocalServices()
    .AddExternalServices(builder.Configuration, builder.Environment);

builder.Services.AddCodeFirstGrpc(config =>
{
    config.ResponseCompressionLevel = System.IO.Compression.CompressionLevel.Optimal;
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
    await app.ApplyMigrationsAsync();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

if (TestAppUserSeeder.ShouldRun(app.Environment, app.Configuration))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider
        .GetRequiredService<TestAppUserSeeder>()
        .SeedAsync();
}

app.UseHttpsRedirection();
// Configure localization middleware
var supportedCultures = new[] {
    new CultureInfo("en"), new CultureInfo("pl")
};
app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new Microsoft.AspNetCore.Localization.RequestCulture("en"),
    SupportedCultures = supportedCultures,
    SupportedUICultures = supportedCultures,
    ApplyCurrentCultureToResponseHeaders = true
});

app.UseStaticFiles();

app.UseAuthentication();

// The member (Cookies/OIDC) scheme is the default, so UseAuthentication only populates
// HttpContext.User for signed-in members. Guests authenticate via a separate Guest cookie that is
// never the default scheme; surface that identity here so ordinary gRPC/HTTP requests (e.g.
// AuthGrpcService.GetCurrentUser) see an authenticated guest instead of an anonymous user and the
// client stops redirecting guests to the member login.
app.Use(async (context, next) =>
{
    if (context.User?.Identity?.IsAuthenticated != true)
    {
        var guest = await context.AuthenticateAsync(GuestAuthentication.SchemeName);
        if (guest.Succeeded)
        {
            context.User = guest.Principal;
        }
    }

    await next();
});

app.UseAuthorization();

var useTestScheme = app.Configuration.GetValue<bool>("Authentication:UseTestScheme");
var microsoftAuth = app.Configuration.GetSection("Authentication:Microsoft");
var isOidcConfigured = !useTestScheme
    && !string.IsNullOrWhiteSpace(microsoftAuth["TenantId"])
    && !string.IsNullOrWhiteSpace(microsoftAuth["ClientId"]);

app.MapGet("/auth/login", async (string? returnUrl, HttpContext httpContext) =>
{
    var target = ResolveLocalReturnUrl(httpContext.Request, returnUrl);
    if (useTestScheme)
    {
        var cookieOptions = CreateTestCookieOptions(httpContext.Request);
        httpContext.Response.Cookies.Delete(
            TestAuthenticationHandler.UserSelectionCookie,
            cookieOptions);
        httpContext.Response.Cookies.Delete(
            TestAuthenticationHandler.GuestSessionCookie,
            cookieOptions);
        await httpContext.SignOutAsync(GuestAuthentication.SchemeName);
        return Results.LocalRedirect(target);
    }

    return Results.Challenge(new AuthenticationProperties { RedirectUri = target });
});

app.MapGet("/auth/logout", async (HttpContext httpContext) =>
{
    if (useTestScheme)
    {
        var cookieOptions = CreateTestCookieOptions(httpContext.Request);
        httpContext.Response.Cookies.Append(
            TestAuthenticationHandler.UserSelectionCookie,
            TestAuthenticationHandler.AnonymousSelection,
            cookieOptions);
        httpContext.Response.Cookies.Delete(
            TestAuthenticationHandler.GuestSessionCookie,
            cookieOptions);
        await httpContext.SignOutAsync(GuestAuthentication.SchemeName);
        return Results.LocalRedirect("/");
    }

    if (PlanDeckIdentity.IsValidGuest(httpContext.User))
    {
        await httpContext.SignOutAsync(GuestAuthentication.SchemeName);
        return Results.LocalRedirect("/");
    }

    await httpContext.SignOutAsync(GuestAuthentication.SchemeName);
    var schemes = isOidcConfigured
        ? new[] { CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme }
        : new[] { CookieAuthenticationDefaults.AuthenticationScheme };

    return Results.SignOut(new AuthenticationProperties { RedirectUri = "/" }, schemes);
});

// Anonymous guest redeem: exchange a share code + temporary name for a session-scoped guest cookie.
app.MapPost("/guest/join", async (
    GuestJoinRequest request,
    HttpContext httpContext,
    ISessionRepository sessions,
    CancellationToken cancellationToken) =>
{
    var displayName = request.DisplayName?.Trim();
    if (string.IsNullOrEmpty(displayName) || displayName.Length > 40)
    {
        return Results.BadRequest();
    }

    var code = request.Code?.Trim();
    if (string.IsNullOrEmpty(code))
    {
        return Results.NotFound();
    }

    var session = await sessions.GetActiveSessionByShareCodeAsync(code, cancellationToken);
    if (session is null)
    {
        return await sessions.ShareCodeExistsAsync(code, cancellationToken)
            ? Results.Conflict()
            : Results.NotFound();
    }

    var principal = GuestAuthentication.BuildPrincipal(
        Guid.NewGuid(), session.TenantId, displayName, session.SessionId);
    await httpContext.SignInAsync(
        GuestAuthentication.SchemeName,
        principal,
        new AuthenticationProperties { IsPersistent = true });

    return Results.Ok(new GuestJoinResponse(session.SessionId));
}).AllowAnonymous();

if (E2eScenarioEndpoints.ShouldMap(app.Environment, app.Configuration))
{
    app.MapE2eScenarioEndpoints();
}


// Configure the HTTP request pipeline.
app.UseGrpcWeb(new GrpcWebOptions { DefaultEnabled = true });
app.MapGrpcService<HelloGrpcService>()
    .RequireAuthorization(PlanDeckPolicies.MemberAccount);
app.MapGrpcService<AzureDevOpsWorkItemGrpcService>()
    .RequireAuthorization(PlanDeckPolicies.MemberAccount);
app.MapGrpcService<TeamGrpcService>()
    .RequireAuthorization(PlanDeckPolicies.MemberAccount);
app.MapGrpcService<ProjectGrpcService>()
    .RequireAuthorization(PlanDeckPolicies.MemberAccount);
app.MapGrpcService<SessionGrpcService>()
    .RequireAuthorization(PlanDeckPolicies.RoomIdentity);
app.MapGrpcService<SessionMemberGrpcService>()
    .RequireAuthorization(PlanDeckPolicies.MemberAccount);
app.MapGrpcService<AuthGrpcService>()
    .AllowAnonymous();
app.MapHub<PlanningRoomHub>("/hubs/planning-room")
    .RequireAuthorization(PlanDeckPolicies.RoomIdentity);
app.MapStaticAssets();
app.MapDefaultEndpoints();
app.MapFallbackToFile("index.html");
//app.MapGrpcService<GreeterService>();

app.Run();

static CookieOptions CreateTestCookieOptions(HttpRequest request) =>
    new()
    {
        HttpOnly = true,
        IsEssential = true,
        Path = "/",
        SameSite = SameSiteMode.Lax,
        Secure = request.IsHttps
    };

static string ResolveLocalReturnUrl(HttpRequest request, string? returnUrl)
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


namespace PlanDeck.Server
{
    public sealed class ServerEntryPoint;
}
