using System.Threading.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using PlanDeck.Application.Abstractions;
using PlanDeck.Application.Planning;
using PlanDeck.Application.Services;
using PlanDeck.Common.Identity;
using PlanDeck.Infrastructure.AzureDevOps;
using PlanDeck.Infrastructure.Identity;
using PlanDeck.Infrastructure.Persistence;
using PlanDeck.Server.Identity;
using PlanDeck.Server.Realtime;
using PlanDeck.Server.Testing;
using Microsoft.Extensions.DependencyInjection.Extensions;

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

            ConfigureIdentity(services);

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
                services.RemoveAll<IProjectSecretStore>();
                services.AddSingleton<IProjectSecretStore, InMemoryProjectSecretStore>();
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
                // Phase 4 will re-introduce a multi-tenant Entra flow with explicit account
                // linking. For Phase 1 the OIDC handler is left in place but does not yet
                // provision a PlanDeck profile, so sign-in via Entra is not active.
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
                });
            }

            AddPlanDeckAuthorization(services);
            services.AddHttpClient<IAzureDevOpsWorkItemClient, AzureDevOpsWorkItemClient>();
            services.AddScoped<IAdoConnectionContextResolver, AdoConnectionContextResolver>();

            return services;
        }

        public IServiceCollection AddAccountRateLimiting(IConfiguration configuration)
        {
            var disabled = configuration.GetValue<bool>("RateLimiting:Disable");

            services.AddRateLimiter(options =>
            {
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

                var registerLimit = disabled ? int.MaxValue : 3;
                var registerWindow = disabled ? TimeSpan.FromMilliseconds(1) : TimeSpan.FromMinutes(10);
                options.AddFixedWindowLimiter("register", configureOptions =>
                {
                    configureOptions.PermitLimit = registerLimit;
                    configureOptions.Window = registerWindow;
                    configureOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                    configureOptions.QueueLimit = 0;
                });

                var loginLimit = disabled ? int.MaxValue : 10;
                var loginWindow = disabled ? TimeSpan.FromMilliseconds(1) : TimeSpan.FromMinutes(1);
                options.AddFixedWindowLimiter("login", configureOptions =>
                {
                    configureOptions.PermitLimit = loginLimit;
                    configureOptions.Window = loginWindow;
                    configureOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                    configureOptions.QueueLimit = 0;
                });

                var resendLimit = disabled ? int.MaxValue : 3;
                var resendWindow = disabled ? TimeSpan.FromMilliseconds(1) : TimeSpan.FromMinutes(10);
                options.AddFixedWindowLimiter("resend-confirmation", configureOptions =>
                {
                    configureOptions.PermitLimit = resendLimit;
                    configureOptions.Window = resendWindow;
                    configureOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                    configureOptions.QueueLimit = 0;
                });

                var forgotLimit = disabled ? int.MaxValue : 3;
                var forgotWindow = disabled ? TimeSpan.FromMilliseconds(1) : TimeSpan.FromMinutes(10);
                options.AddFixedWindowLimiter("forgot-password", configureOptions =>
                {
                    configureOptions.PermitLimit = forgotLimit;
                    configureOptions.Window = forgotWindow;
                    configureOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                    configureOptions.QueueLimit = 0;
                });
            });

            return services;
        }

        public IServiceCollection AddLocalServices()
        {
            services.AddAntiforgery();
            services.AddMemoryCache();
            services.AddHttpContextAccessor();
            services.AddSingleton(TimeProvider.System);
            services.AddScoped<RequestPrincipalAccessor>();
            services.AddScoped<ICurrentUserContext, HttpContextCurrentUserContext>();
            services.AddScoped<IProvisioningContextAccessor, ProvisioningContextAccessor>();
            services.AddScoped<IIdentityAccountRepository, IdentityAccountRepository>();
            services.AddScoped<IAccountProvisioningService, AccountProvisioningService>();
            services.AddScoped<ILocalAccountService, LocalAccountService>();
            services.AddScoped<ICookieSessionValidator, CookieSessionValidator>();
            services.AddScoped<IAppUserRepository, AppUserRepository>();
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

    private static void ConfigureIdentity(IServiceCollection services)
    {
        services.AddIdentityCore<ApplicationUser>(options =>
        {
            options.User.RequireUniqueEmail = true;
            options.Password.RequireDigit = true;
            options.Password.RequireLowercase = true;
            options.Password.RequireUppercase = true;
            options.Password.RequireNonAlphanumeric = true;
            options.Password.RequiredLength = 12;
            options.Lockout.AllowedForNewUsers = true;
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
            options.SignIn.RequireConfirmedEmail = false;
        })
        .AddRoles<IdentityRole<Guid>>()
        .AddEntityFrameworkStores<PlanDeckDbContext>()
        .AddDefaultTokenProviders();

        services.AddScoped<SignInManager<ApplicationUser>>();
        services.AddScoped<IUserConfirmation<ApplicationUser>, DefaultUserConfirmation<ApplicationUser>>();

        services.Replace(ServiceDescriptor.Scoped<
            IUserClaimsPrincipalFactory<ApplicationUser>,
            PlanDeckUserClaimsPrincipalFactory>());
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
                    PlanDeckClaimTypes.UserId,
                    out var userId))
            {
                context.RejectPrincipal();
                return;
            }

            var validator = context.HttpContext.RequestServices
                .GetRequiredService<ICookieSessionValidator>();
            if (!await validator.IsValidAsync(principal!, context.HttpContext.RequestAborted))
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



