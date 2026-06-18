using PlanDeck.Server.Extensions;
using PlanDeck.Application.Services;
using PlanDeck.Server.Hubs;
using ProtoBuf.Grpc.Server;
using System.Globalization;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");
builder.Services.AddGrpc();
builder.Services.AddSignalR();

builder.Services
    .AddSqlDatabase(builder.Configuration)
    .AddLocalServices()
    .AddExternalServices(builder.Configuration);

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

app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();


// Configure the HTTP request pipeline.
app.UseGrpcWeb(new GrpcWebOptions { DefaultEnabled = true });
app.MapGrpcService<HelloGrpcService>();
app.MapGrpcService<AzureDevOpsWorkItemGrpcService>();
app.MapGrpcService<TeamGrpcService>();
app.MapHub<PlanningRoomHub>("/hubs/planning-room");
app.MapStaticAssets();
app.MapDefaultEndpoints();
app.MapFallbackToFile("index.html");
//app.MapGrpcService<GreeterService>();

app.Run();
