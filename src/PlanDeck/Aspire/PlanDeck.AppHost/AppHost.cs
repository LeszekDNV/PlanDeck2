using Azure.Provisioning.AppContainers;
using Azure.Provisioning.KeyVault;
using Azure.Provisioning.Sql;
using Microsoft.Extensions.Configuration;

var builder = DistributedApplication.CreateBuilder(args);

const string PublishTargetConfigurationKey = "Publishing:Target";
const string PublishTargetTesting = "Testing";
const string PublishTargetProduction = "Production";

var publishTarget = ResolvePublishTarget(
    builder.Configuration,
    builder.ExecutionContext.IsPublishMode,
    Environment.GetEnvironmentVariable("PLANDECK_PUBLISH_TARGET"));
var azureEnvironmentName = builder.Configuration["AZURE_ENV_NAME"];

var useLocalE2eTestAuth = string.Equals(
    Environment.GetEnvironmentVariable("PLANDECK_E2E_TESTAUTH"),
    "true",
    StringComparison.OrdinalIgnoreCase);

var isNamedTestingEnvironment = !string.IsNullOrWhiteSpace(azureEnvironmentName)
    && (string.Equals(azureEnvironmentName, "test", StringComparison.OrdinalIgnoreCase)
        || azureEnvironmentName.Contains("testing", StringComparison.OrdinalIgnoreCase));

var usePublishTestAuth = builder.ExecutionContext.IsPublishMode
    && (useLocalE2eTestAuth
        || string.Equals(
            publishTarget,
            PublishTargetTesting,
            StringComparison.OrdinalIgnoreCase)
        || isNamedTestingEnvironment);

var useE2eTestAuth = useLocalE2eTestAuth || usePublishTestAuth;

var planDeckServer = builder.AddProject<Projects.PlanDeck_Server>("plandeck-server");
if (!builder.ExecutionContext.IsPublishMode || !usePublishTestAuth)
{
    planDeckServer = planDeckServer.WithExternalHttpEndpoints();
}
else
{
    planDeckServer = planDeckServer
        .WithEndpoint("http", endpoint => endpoint.IsExternal = false)
        .WithEndpoint("https", endpoint => endpoint.IsExternal = false);
}

if (!useE2eTestAuth)
{
    var keyVault = builder.AddAzureKeyVault("key-vault")
        .ClearDefaultRoleAssignments()
        .ConfigureInfrastructure(infrastructure =>
        {
            var vault = infrastructure.GetProvisionableResources()
                .OfType<KeyVaultService>()
                .Single();
            vault.Properties.EnableSoftDelete = true;
            vault.Properties.EnablePurgeProtection = true;
        });

    planDeckServer
        .WithRoleAssignments(keyVault, KeyVaultBuiltInRole.KeyVaultSecretsOfficer)
        .WithReference(keyVault)
        .WaitFor(keyVault);
}

if (builder.ExecutionContext.IsPublishMode)
{
    _ = builder.AddAzureContainerAppEnvironment("aca-env");

    var sqlServer = builder.AddAzureSqlServer("sql-server");
    var sqlDatabase = sqlServer.AddDatabase("PlanDeckDb");

    // Pin the pilot database to a serverless General Purpose tier with auto-pause to keep
    // cost minimal; cold-start latency on the first query after a pause is acceptable for a
    // validation environment (the runbook warms the DB before timing-sensitive checks).
    sqlServer.ConfigureInfrastructure(infrastructure =>
    {
        var database = infrastructure.GetProvisionableResources().OfType<SqlDatabase>().Single();
        database.Sku = new SqlSku
        {
            Name = "GP_S_Gen5_1",
            Tier = "GeneralPurpose",
            Family = "Gen5",
            Capacity = 1
        };
        database.MinCapacity = 0.5;
        database.AutoPauseDelay = 60;
    });

    planDeckServer
        .WithReference(sqlDatabase, "DefaultConnection")
        .WithEnvironment("EmailSettings__Host", "smtp")
        .WithEnvironment("EmailSettings__Port", "587");

    if (usePublishTestAuth)
    {
        var e2eScenarioToken = builder.Configuration["E2E_SCENARIO_TOKEN"]
            ?? builder.Configuration["Testing:E2eScenario:AuthorizationToken"]
            ?? string.Empty;

        planDeckServer
            .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Testing")
            .WithEnvironment("Authentication__UseTestScheme", "true")
            .WithEnvironment("Testing__E2eScenario__AuthorizationToken", e2eScenarioToken);
    }
    else
    {
        var entraTenantId = builder.Configuration["AZURE_ENTRA_TENANT_ID"]
            ?? builder.Configuration["Authentication:Microsoft:TenantId"]
            ?? string.Empty;
        var entraClientId = builder.Configuration["AZURE_ENTRA_CLIENT_ID"]
            ?? builder.Configuration["Authentication:Microsoft:ClientId"]
            ?? string.Empty;
        var entraClientSecret = builder.Configuration["AZURE_ENTRA_CLIENT_SECRET"]
            ?? builder.Configuration["Authentication:Microsoft:ClientSecret"]
            ?? string.Empty;

        planDeckServer
            .WithEnvironment("Authentication__Microsoft__TenantId", entraTenantId)
            .WithEnvironment("Authentication__Microsoft__ClientId", entraClientId)
            .WithEnvironment("Authentication__Microsoft__ClientSecret", entraClientSecret);
    }

    planDeckServer
        .WaitFor(sqlDatabase)
        .PublishAsAzureContainerApp((infrastructure, app) =>
        {
            app.Configuration.Ingress.External = !usePublishTestAuth;

            // SignalR room state is in-process (singleton IPlanningRoomService, no backplane),
            // so the pilot must run as a single pinned replica with session affinity: rooms
            // survive across requests and ACA never scales the app to zero. Raising MaxReplicas
            // above 1 silently breaks room state until a backplane is added.
            app.Template.Scale.MinReplicas = 1;
            app.Template.Scale.MaxReplicas = 1;
            app.Configuration.Ingress.StickySessionsAffinity = StickySessionAffinity.Sticky;
        });
}
else
{
    // A fixed local https port keeps the dev URL stable; ACA ingress only supports 443 for
    // https, so this fixed port must not be applied in publish mode.
    planDeckServer.WithEndpoint("https", endpoint => endpoint.Port = 7443);

    var sqlDatabase = builder.AddSqlServer("sql-server", port: 2140)
        .WithImage("mssql/server:2025-latest")
        .WithDataVolume()
        .WithLifetime(ContainerLifetime.Persistent)
        .AddDatabase("PlanDeckDb");

    var mailSmtp = builder.AddMailPit("smtp", 1080, 1025);

    planDeckServer
        .WithReference(sqlDatabase, "DefaultConnection")
        // Local runs should use developer credentials instead of probing managed identity,
        // because stale MSI certs on a workstation can break DefaultAzureCredential.
        .WithEnvironment("AZURE_TOKEN_CREDENTIALS", "AzureCliCredential")
        .WithEnvironment("EmailSettings__Host", "localhost")
        .WithEnvironment("EmailSettings__Port", "1025")
        .WaitFor(sqlDatabase)
        .WaitFor(mailSmtp);
}

// The E2E fixture sets this environment variable to drive the UI with a deterministic
// test-auth scheme instead of interactive Entra sign-in. No effect on normal `dotnet run`.
if (string.Equals(
        Environment.GetEnvironmentVariable("PLANDECK_E2E_TESTAUTH"),
        "true",
        StringComparison.OrdinalIgnoreCase))
{
    planDeckServer.WithEnvironment("Authentication__UseTestScheme", "true");
}

var scenarioToken = Environment.GetEnvironmentVariable("PLANDECK_E2E_SCENARIO_TOKEN");
if (!string.IsNullOrWhiteSpace(scenarioToken))
{
    planDeckServer.WithEnvironment("Testing__E2eScenario__AuthorizationToken", scenarioToken);
}

builder.Build().Run();

static string ResolvePublishTarget(
    IConfiguration configuration,
    bool isPublishMode,
    string? publishTargetOverride)
{
    var configured = string.IsNullOrWhiteSpace(publishTargetOverride)
        ? configuration[PublishTargetConfigurationKey]
        : publishTargetOverride;
    if (string.IsNullOrWhiteSpace(configured))
    {
        return PublishTargetProduction;
    }

    if (string.Equals(configured, PublishTargetTesting, StringComparison.OrdinalIgnoreCase))
    {
        return PublishTargetTesting;
    }

    if (string.Equals(configured, PublishTargetProduction, StringComparison.OrdinalIgnoreCase))
    {
        return PublishTargetProduction;
    }

    if (isPublishMode)
    {
        throw new InvalidOperationException(
            $"Unsupported {PublishTargetConfigurationKey} value '{configured}'. "
            + $"Expected '{PublishTargetProduction}' or '{PublishTargetTesting}'.");
    }

    return PublishTargetProduction;
}
