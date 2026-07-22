using PlanDeck.Server.Identity;

namespace PlanDeck.Server.Testing;

public static class E2eScenarioEndpoints
{
    public const string TokenHeaderName = "X-PlanDeck-Test-Token";
    private const string ConfigurationTokenPath = "Testing:E2eScenario:AuthorizationToken";

    public static bool ShouldMap(IHostEnvironment environment, IConfiguration configuration)
    {
        if (!configuration.GetValue<bool>("Authentication:UseTestScheme"))
        {
            return false;
        }

        if (!environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(configuration[ConfigurationTokenPath]);
    }

    public static IEndpointRouteBuilder MapE2eScenarioEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/testing/e2e-scenarios")
            .RequireAuthorization(PlanDeckPolicies.MemberAccount);

        group.MapPost("/", async (
                E2eScenarioSeedRequest request,
                HttpContext httpContext,
                IConfiguration configuration,
                E2eScenarioService service,
                CancellationToken cancellationToken) =>
            {
                if (!IsTokenAuthorized(httpContext, configuration))
                {
                    return Results.Unauthorized();
                }

                try
                {
                    var scenario = await service.SeedAsync(request, cancellationToken);
                    return Results.Ok(scenario);
                }
                catch (InvalidOperationException error)
                {
                    return Results.BadRequest(error.Message);
                }
            })
            .WithName("SeedE2eScenario");

        group.MapDelete("/{runId:guid}", async (
                Guid runId,
                HttpContext httpContext,
                IConfiguration configuration,
                E2eScenarioService service,
                CancellationToken cancellationToken) =>
            {
                if (!IsTokenAuthorized(httpContext, configuration))
                {
                    return Results.Unauthorized();
                }

                try
                {
                    var cleanup = await service.CleanupAsync(runId, cancellationToken);
                    return Results.Ok(cleanup);
                }
                catch (InvalidOperationException error)
                {
                    return Results.BadRequest(error.Message);
                }
            })
            .WithName("CleanupE2eScenario");

        return endpoints;
    }

    private static bool IsTokenAuthorized(HttpContext httpContext, IConfiguration configuration)
    {
        var expectedToken = configuration[ConfigurationTokenPath];
        if (string.IsNullOrWhiteSpace(expectedToken))
        {
            return false;
        }

        if (!httpContext.Request.Headers.TryGetValue(TokenHeaderName, out var providedToken))
        {
            return false;
        }

        return string.Equals(
            providedToken.ToString(),
            expectedToken,
            StringComparison.Ordinal);
    }
}
