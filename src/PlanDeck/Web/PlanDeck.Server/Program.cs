using PlanDeck.Server.Extensions;
using PlanDeck.Application.Abstractions;
using PlanDeck.Application.Services;
using PlanDeck.Server.Hubs;
using PlanDeck.Server.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using ProtoBuf.Grpc.Server;
using System.Globalization;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddAzureKeyVaultClient("key-vault");

// Add services to the container.
builder.Services.AddLocalization();
builder.Services.AddGrpc();
builder.Services.AddSignalR();

builder.Services
    .AddSqlDatabase(builder.Configuration)
    .AddLocalServices()
    .AddExternalServices(builder.Configuration, builder.Environment)
    .AddAccountRateLimiting(builder.Configuration);

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

app.UseAuthorization();

app.MapGet("/auth/login", (string? returnUrl, HttpContext httpContext) =>
{
    var target = ResolveLocalReturnUrl(httpContext.Request, returnUrl);
    return Results.Challenge(new AuthenticationProperties { RedirectUri = target });
});

// GET /auth/logout is intentionally restricted to non-interactive identities (guest and test
// scheme). Authenticated members must use POST /account/logout with antiforgery.
app.MapGet("/auth/logout", async (HttpContext httpContext) =>
{
    if (PlanDeckIdentity.IsGuest(httpContext.User))
    {
        await httpContext.SignOutAsync(GuestAuthentication.SchemeName);
        return Results.LocalRedirect("/");
    }

    return Results.NotFound();
});

app.MapAccountEndpoints();

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

    public partial class Program;
}



