using PlanDeck.Server.Extensions;
using PlanDeck.Application.Abstractions;
using PlanDeck.Application.Services;
using PlanDeck.Server.Hubs;
using PlanDeck.Server.Identity;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using ProtoBuf.Grpc.Server;
using System.Globalization;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
var keyVaultConfigured =
    !string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("key-vault"))
    || !string.IsNullOrWhiteSpace(
        builder.Configuration["Aspire:Azure:Security:KeyVault:VaultUri"]);
builder.AddAzureKeyVaultClient(
    "key-vault",
    settings => settings.DisableHealthChecks = !keyVaultConfigured);

// Add services to the container.
builder.Services.AddLocalization();
builder.Services.AddGrpc();
builder.Services.AddSignalR();

builder.Services
    .AddSqlDatabase(builder.Configuration)
    .AddLocalServices(keyVaultConfigured)
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
    app.UseExceptionHandler();
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

app.UseAuthentication();
app.UseAuthorization();

// Keep the removed mutating route as an explicit tombstone so the SPA fallback cannot return 200.
app.MapGet("/auth/logout", static () => Results.NotFound())
    .ExcludeFromDescription();

app.MapAccountEndpoints(
    app.Services.GetRequiredService<MicrosoftAuthenticationOptions>());

app.MapPost("/guest/logout", async (
    IAntiforgery antiforgery,
    HttpContext httpContext) =>
{
    if (!await antiforgery.IsRequestValidAsync(httpContext))
    {
        return Results.BadRequest(new
        {
            Status = "InvalidAntiForgeryToken",
            Errors = new[] { "Invalid antiforgery token." }
        });
    }

    var guestAuthentication = await httpContext.AuthenticateAsync(GuestAuthentication.SchemeName);
    if (!guestAuthentication.Succeeded
        || !PlanDeckIdentity.IsGuest(guestAuthentication.Principal))
    {
        return Results.NotFound();
    }

    await httpContext.SignOutAsync(GuestAuthentication.SchemeName);
    return Results.Ok(new { Status = "Success", ReturnUrl = "/" });
}).AllowAnonymous();

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

namespace PlanDeck.Server
{
    public sealed class ServerEntryPoint;

    public partial class Program;
}
