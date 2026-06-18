using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.AspNetCore.Components.Authorization;
using Grpc.Net.Client;
using Grpc.Net.Client.Web;
using MudBlazor.Services;
using PlanDeck.Client;
using PlanDeck.Client.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddMudServices();
builder.Services.AddAuthorizationCore();
builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

builder.Services.AddScoped(sp => GrpcChannel.ForAddress(
    builder.HostEnvironment.BaseAddress,
    new GrpcChannelOptions { HttpHandler = new GrpcWebHandler(new HttpClientHandler()) }));
builder.Services.AddScoped<IHelloClientService, HelloClientService>();
builder.Services.AddScoped<IAzureDevOpsClientService, AzureDevOpsClientService>();
builder.Services.AddScoped<IPlanningRoomClientService, PlanningRoomClientService>();
builder.Services.AddScoped<GrpcAuthenticationStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(sp =>
    sp.GetRequiredService<GrpcAuthenticationStateProvider>());

await builder.Build().RunAsync();
