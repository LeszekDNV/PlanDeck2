using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using PlanDeck.Application.Abstractions;
using PlanDeck.Application.Planning;
using PlanDeck.Application.Services;
using PlanDeck.Infrastructure.AzureDevOps;
using PlanDeck.Infrastructure.Persistence;
using PlanDeck.Server.Identity;
using PlanDeck.Server.Realtime;

namespace PlanDeck.Server.Extensions;

public static class ServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddSqlDatabase(IConfiguration configuration)
        {
            services.AddDbContext<PlanDeckDbContext>(options =>
            {
                var connectionString = configuration.GetConnectionString("DefaultConnection");
                if (string.IsNullOrWhiteSpace(connectionString))
                {
                    throw new InvalidOperationException("Connection string 'DefaultConnection' is required.");
                }

                var managedIdentityClientId = configuration["AZURE_CLIENT_ID"];
                if (!string.IsNullOrWhiteSpace(managedIdentityClientId))
                {
                    connectionString = connectionString
                        .Replace(
                            "Authentication=\"Active Directory Default\"",
                            $"Authentication=Active Directory Managed Identity;User Id={managedIdentityClientId}",
                            StringComparison.OrdinalIgnoreCase)
                        .Replace(
                            "Authentication=Active Directory Default",
                            $"Authentication=Active Directory Managed Identity;User Id={managedIdentityClientId}",
                            StringComparison.OrdinalIgnoreCase);
                }

                options.UseSqlServer(connectionString, sqlOptions => sqlOptions.EnableRetryOnFailure());
            });

            services.AddHealthChecks()
                .AddDbContextCheck<PlanDeckDbContext>("sql");

            return services;
        }

        public IServiceCollection AddExternalServices(IConfiguration configuration, IHostEnvironment environment)
        {
            var useTestScheme = configuration.GetValue<bool>("Authentication:UseTestScheme");
            if (useTestScheme && !environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
            {
                throw new InvalidOperationException(
                    "Authentication:UseTestScheme is only permitted in the Development or Testing environments.");
            }

            if (useTestScheme)
            {
                services
                    .AddAuthentication(TestAuthenticationHandler.SchemeName)
                    .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                        TestAuthenticationHandler.SchemeName, null);

                services.AddAuthorization();
                services.Configure<AzureDevOpsOptions>(configuration.GetSection(AzureDevOpsOptions.SectionName));
                services.AddHttpClient<IAzureDevOpsWorkItemClient, AzureDevOpsWorkItemClient>();

                return services;
            }

            var microsoftAuth = configuration.GetSection("Authentication:Microsoft");
            var tenantId = microsoftAuth["TenantId"];
            var clientId = microsoftAuth["ClientId"];
            var callbackPath = microsoftAuth["CallbackPath"];
            var isMicrosoftAuthConfigured = !string.IsNullOrWhiteSpace(tenantId)
                && !string.IsNullOrWhiteSpace(clientId);

            var authenticationBuilder = services
                .AddAuthentication(options =>
                {
                    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                    options.DefaultChallengeScheme = isMicrosoftAuthConfigured
                        ? OpenIdConnectDefaults.AuthenticationScheme
                        : CookieAuthenticationDefaults.AuthenticationScheme;
                })
                .AddCookie();

            if (isMicrosoftAuthConfigured)
            {
                authenticationBuilder.AddOpenIdConnect(options =>
                {
                    options.Authority = $"https://login.microsoftonline.com/{tenantId}/v2.0";
                    options.ClientId = clientId;
                    options.ClientSecret = microsoftAuth["ClientSecret"];
                    options.CallbackPath = string.IsNullOrWhiteSpace(callbackPath)
                        ? "/signin-oidc"
                        : callbackPath;
                    options.ResponseType = OpenIdConnectResponseType.Code;
                    options.SaveTokens = false;
                    options.GetClaimsFromUserInfoEndpoint = true;
                    options.MapInboundClaims = false;
                });
            }

            services.AddAuthorization();
            services.Configure<AzureDevOpsOptions>(configuration.GetSection(AzureDevOpsOptions.SectionName));
            services.AddHttpClient<IAzureDevOpsWorkItemClient, AzureDevOpsWorkItemClient>();

            return services;
        }

        public IServiceCollection AddLocalServices()
        {
            services.AddHttpContextAccessor();
            services.AddScoped<RequestPrincipalAccessor>();
            services.AddScoped<ICurrentUserContext, HttpContextCurrentUserContext>();
            services.AddSingleton<IPlanningRoomService, PlanningRoomService>();
            services.AddScoped<IPlanningRoomNotifier, SignalRPlanningRoomNotifier>();
            services.AddScoped<IVotingRoundService, VotingRoundService>();
            services.AddScoped<HelloGrpcService>();
            services.AddScoped<AzureDevOpsWorkItemGrpcService>();
            services.AddScoped<ITeamRepository, TeamRepository>();
            services.AddScoped<TeamGrpcService>();
            services.AddScoped<ISessionRepository, SessionRepository>();
            services.AddScoped<SessionGrpcService>();
            services.AddScoped<ISessionMemberRepository, SessionMemberRepository>();
            services.AddScoped<SessionMemberGrpcService>();
            services.AddScoped<AuthGrpcService>();
            return services;
        }
    }



    public static async Task<WebApplication> ApplyMigrationsAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PlanDeckDbContext>();

        await dbContext.Database.MigrateAsync();

        return app;
    }
}
