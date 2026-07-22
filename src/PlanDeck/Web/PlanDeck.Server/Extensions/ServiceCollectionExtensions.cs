using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using System.Security.Claims;
using PlanDeck.Application.Abstractions;
using PlanDeck.Application.Planning;
using PlanDeck.Application.Services;
using PlanDeck.Infrastructure.AzureDevOps;
using PlanDeck.Infrastructure.Persistence;
using PlanDeck.Server.Identity;
using PlanDeck.Server.Realtime;
using PlanDeck.Server.Testing;

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
            services.AddHttpClient<IAzureDevOpsConnectionValidator, AzureDevOpsConnectionValidator>(
                client => client.Timeout = TimeSpan.FromSeconds(20));
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
                        TestAuthenticationHandler.SchemeName, null)
                    .AddCookie(GuestAuthentication.SchemeName, GuestAuthentication.ConfigureCookie);

                AddPlanDeckAuthorization(services);
                services.AddScoped<IAzureDevOpsWorkItemClient, FakeAzureDevOpsWorkItemClient>();
                services.AddScoped<IAdoConnectionContextResolver, FakeAdoConnectionContextResolver>();

                return services;
            }

            var microsoftAuth = configuration.GetSection("Authentication:Microsoft");
            var tenantId = microsoftAuth["TenantId"];
            var clientId = microsoftAuth["ClientId"];
            var clientSecret = microsoftAuth["ClientSecret"];
            var callbackPath = microsoftAuth["CallbackPath"];
            var isMicrosoftAuthConfigured = !string.IsNullOrWhiteSpace(tenantId)
                && !string.IsNullOrWhiteSpace(clientId)
                && !string.IsNullOrWhiteSpace(clientSecret);

            if (environment.IsProduction() && !isMicrosoftAuthConfigured)
            {
                throw new InvalidOperationException(
                    "Production requires Authentication:Microsoft:TenantId, ClientId, and ClientSecret.");
            }

            var authenticationBuilder = services
                .AddAuthentication(options =>
                {
                    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                    options.DefaultChallengeScheme = isMicrosoftAuthConfigured
                        ? OpenIdConnectDefaults.AuthenticationScheme
                        : CookieAuthenticationDefaults.AuthenticationScheme;
                })
                .AddCookie(ConfigureMemberCookie)
                .AddCookie(GuestAuthentication.SchemeName, GuestAuthentication.ConfigureCookie);

            if (isMicrosoftAuthConfigured)
            {
                authenticationBuilder.AddOpenIdConnect(options =>
                {
                    options.Authority = $"https://login.microsoftonline.com/{tenantId}/v2.0";
                    options.ClientId = clientId;
                    options.ClientSecret = clientSecret;
                    options.CallbackPath = string.IsNullOrWhiteSpace(callbackPath)
                        ? "/signin-oidc"
                        : callbackPath;
                    options.ResponseType = OpenIdConnectResponseType.Code;
                    options.SaveTokens = false;
                    options.GetClaimsFromUserInfoEndpoint = true;
                    options.MapInboundClaims = false;
                    options.Events.OnTokenValidated = async context =>
                    {
                        var principal = context.Principal;
                        if (principal?.Identity is not ClaimsIdentity identity)
                        {
                            context.Fail("The authenticated identity is unavailable.");
                            return;
                        }

                        try
                        {
                            var provisioner = context.HttpContext.RequestServices
                                .GetRequiredService<IAppUserProvisioner>();
                            var appUserId = await provisioner.ProvisionAsync(
                                principal,
                                context.HttpContext.RequestAborted);

                            identity.AddClaim(new Claim(
                                PlanDeckIdentity.AppUserIdClaim,
                                appUserId.ToString()));
                            identity.AddClaim(new Claim(
                                PlanDeckIdentity.ActiveUserClaim,
                                bool.TrueString));
                        }
                        catch (InvalidOperationException)
                        {
                            context.Fail("The authenticated PlanDeck account is invalid or inactive.");
                        }
                    };
                });
            }

            AddPlanDeckAuthorization(services);
            services.AddHttpClient<IAzureDevOpsWorkItemClient, AzureDevOpsWorkItemClient>();
            services.AddScoped<IAdoConnectionContextResolver, AdoConnectionContextResolver>();

            return services;
        }

        public IServiceCollection AddLocalServices()
        {
            services.AddMemoryCache();
            services.AddHttpContextAccessor();
            services.AddSingleton(TimeProvider.System);
            services.AddScoped<RequestPrincipalAccessor>();
            services.AddScoped<ICurrentUserContext, HttpContextCurrentUserContext>();
            services.AddScoped<IAppUserRepository, AppUserRepository>();
            services.AddScoped<IAppUserProvisioner, AppUserProvisioner>();
            services.AddScoped<TestAppUserSeeder>();
            services.AddScoped<E2eScenarioService>();
            services.AddSingleton<IPlanningRoomService, PlanningRoomService>();
            services.AddHostedService<PlanningRoomCleanupService>();
            services.AddScoped<IPlanningRoomNotifier, SignalRPlanningRoomNotifier>();
            services.AddScoped<IVotingRoundService, VotingRoundService>();
            services.AddScoped<AzureDevOpsWorkItemGrpcService>();
            services.AddScoped<ITeamRepository, TeamRepository>();
            services.AddScoped<TeamGrpcService>();
            services.AddScoped<IProjectRepository, ProjectRepository>();
            services.AddScoped<
                IProjectAzureDevOpsConnectionRepository,
                ProjectAzureDevOpsConnectionRepository>();
            services.AddScoped<IProjectAccessResolver, ProjectAccessResolver>();
            services.AddSingleton<IProjectSecretStore, KeyVaultProjectSecretStore>();
            services.AddScoped<ISessionAccessResolver, SessionAccessResolver>();
            services.AddScoped<ProjectGrpcService>();
            services.AddScoped<ISessionRepository, SessionRepository>();
            services.AddSingleton<IShareCodeGenerator, ShareCodeGenerator>();
            services.AddScoped<SessionGrpcService>();
            services.AddScoped<ISessionMemberRepository, SessionMemberRepository>();
            services.AddScoped<SessionMemberGrpcService>();
            services.AddScoped<AuthGrpcService>();
            return services;
        }
    }

    private static void AddPlanDeckAuthorization(IServiceCollection services)
    {
        services.AddAuthorization(options =>
        {
            options.AddPolicy(PlanDeckPolicies.MemberAccount, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireAssertion(context => PlanDeckIdentity.IsValidMember(context.User));
            });
            options.AddPolicy(PlanDeckPolicies.RoomIdentity, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireAssertion(context => PlanDeckIdentity.IsValidRoomIdentity(context.User));
            });
        });
    }

    private static void ConfigureMemberCookie(CookieAuthenticationOptions options)
    {
        options.Events.OnValidatePrincipal = async context =>
        {
            var principal = context.Principal;
            if (!PlanDeckIdentity.IsValidMember(principal)
                || !PlanDeckIdentity.TryReadGuid(
                    principal!,
                    PlanDeckIdentity.AppUserIdClaim,
                    out var appUserId))
            {
                context.RejectPrincipal();
                return;
            }

            var provisioner = context.HttpContext.RequestServices
                .GetRequiredService<IAppUserProvisioner>();
            if (!await provisioner.IsActiveAsync(
                    principal!,
                    appUserId,
                    context.HttpContext.RequestAborted))
            {
                context.RejectPrincipal();
            }
        };
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    }



    public static async Task<WebApplication> ApplyMigrationsAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PlanDeckDbContext>();

        await dbContext.Database.MigrateAsync();

        return app;
    }
}
