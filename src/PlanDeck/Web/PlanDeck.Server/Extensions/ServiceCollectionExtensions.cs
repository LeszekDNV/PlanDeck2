using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using PlanDeck.Application.Abstractions;
using PlanDeck.Application.Planning;
using PlanDeck.Application.Services;
using PlanDeck.Common.Identity;
using PlanDeck.Infrastructure.AzureDevOps;
using PlanDeck.Infrastructure.Identity;
using PlanDeck.Infrastructure.Persistence;
using PlanDeck.Server.Identity;
using PlanDeck.Server.Diagnostics;
using PlanDeck.Server.Realtime;
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
            ConfigureEmailServices(services, configuration, environment);

            var microsoftAuthentication = configuration
                .GetSection(MicrosoftAuthenticationOptions.SectionName)
                .Get<MicrosoftAuthenticationOptions>()
                ?? new MicrosoftAuthenticationOptions();
            microsoftAuthentication.Validate();
            services.AddSingleton(microsoftAuthentication);
            services.AddSingleton<IAuthenticationCapabilities>(microsoftAuthentication);

            var authenticationBuilder = services
                .AddAuthentication(options =>
                {
                    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                    options.DefaultChallengeScheme = microsoftAuthentication.IsAvailable
                        ? OpenIdConnectDefaults.AuthenticationScheme
                        : CookieAuthenticationDefaults.AuthenticationScheme;
                })
                .AddCookie(ConfigureMemberCookie)
                .AddCookie(GuestAuthentication.SchemeName, GuestAuthentication.ConfigureCookie);

            if (microsoftAuthentication.IsAvailable)
            {
                authenticationBuilder.AddOpenIdConnect(options =>
                {
                    options.Authority = "https://login.microsoftonline.com/organizations/v2.0";
                    options.ClientId = microsoftAuthentication.ClientId;
                    options.ClientSecret = microsoftAuthentication.ClientSecret;
                    options.CallbackPath = string.IsNullOrWhiteSpace(microsoftAuthentication.CallbackPath)
                        ? "/signin-oidc"
                        : microsoftAuthentication.CallbackPath;
                    options.ResponseType = OpenIdConnectResponseType.Code;
                    options.SaveTokens = false;
                    options.GetClaimsFromUserInfoEndpoint = true;
                    options.MapInboundClaims = false;

                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        NameClaimType = "name",
                        RoleClaimType = "roles",
                        ValidateIssuer = true,
                        IssuerValidator = EntraIssuerValidator.Validate
                    };

                    options.Events.OnRedirectToIdentityProvider = context =>
                    {
                        var handler = context.HttpContext.RequestServices.GetRequiredService<EntraCallbackHandler>();
                        return handler.OnRedirectToIdentityProviderAsync(context);
                    };

                    options.Events.OnTokenValidated = context =>
                    {
                        var handler = context.HttpContext.RequestServices.GetRequiredService<EntraCallbackHandler>();
                        return handler.OnTokenValidatedAsync(context);
                    };
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

        public IServiceCollection AddLocalServices(bool keyVaultConfigured)
        {
            services.AddProblemDetails();
            services.AddExceptionHandler<GlobalExceptionHandler>();
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
            services.AddScoped<IAccountLifecycleService, AccountLifecycleService>();
            services.AddScoped<IExternalAccountService, ExternalAccountService>();
            services.AddScoped<EntraCallbackHandler>();
            services.AddScoped<ICookieSessionValidator, CookieSessionValidator>();
            services.AddScoped<IAppUserRepository, AppUserRepository>();
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
            if (keyVaultConfigured)
            {
                services.AddSingleton<IProjectSecretStore, KeyVaultProjectSecretStore>();
            }
            else
            {
                services.AddSingleton<IProjectSecretStore, UnavailableProjectSecretStore>();
            }
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

    private static void ConfigureEmailServices(
        IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.Configure<EmailSettings>(configuration.GetSection(EmailSettings.SectionName));
        services.AddScoped<IEmailSender<ApplicationUser>, SmtpEmailSender>();

        if (environment.IsProduction())
        {
            var settings = configuration.GetSection(EmailSettings.SectionName).Get<EmailSettings>();
            if (settings is null
                || string.IsNullOrWhiteSpace(settings.Host)
                || string.IsNullOrWhiteSpace(settings.SenderAddress)
                || string.IsNullOrWhiteSpace(settings.PublicBaseUrl))
            {
                throw new InvalidOperationException(
                    "Production requires EmailSettings:Host, SenderAddress, and PublicBaseUrl.");
            }
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



