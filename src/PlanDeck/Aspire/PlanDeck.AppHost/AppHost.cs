using Aspire.Hosting.Azure;
using Azure.Provisioning;
using Azure.Provisioning.AppContainers;
using Azure.Provisioning.KeyVault;
using Azure.Provisioning.Primitives;
using Azure.Provisioning.Resources;
using Azure.Provisioning.Sql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

var builder = DistributedApplication.CreateBuilder(args);

builder.Services.Configure<AzureProvisioningOptions>(options =>
    options.ProvisioningBuildOptions.InfrastructureResolvers.Add(
        new AzureSqlPowerShellModuleWorkaround()));

const string PublishTargetConfigurationKey = "Publishing:Target";
const string PublishTargetTesting = "Testing";
const string PublishTargetProduction = "Production";

var publishTarget = ResolvePublishTarget(
    builder.Configuration,
    builder.ExecutionContext.IsPublishMode,
    Environment.GetEnvironmentVariable("PLANDECK_PUBLISH_TARGET"));
var azureEnvironmentName = builder.Configuration["AZURE_ENV_NAME"];


var isNamedTestingEnvironment = !string.IsNullOrWhiteSpace(azureEnvironmentName)
    && (string.Equals(azureEnvironmentName, "test", StringComparison.OrdinalIgnoreCase)
        || azureEnvironmentName.Contains("testing", StringComparison.OrdinalIgnoreCase));

var isTestingPublishTarget = builder.ExecutionContext.IsPublishMode
    && (string.Equals(
            publishTarget,
            PublishTargetTesting,
            StringComparison.OrdinalIgnoreCase)
        || isNamedTestingEnvironment);

var planDeckServer = builder
    .AddProject<Projects.PlanDeck_Server>("plandeck-server")
    .WithExternalHttpEndpoints();

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
        .WithEnvironment("EmailSettings__Port", "587")
        .WithEnvironment("EmailSettings__SenderAddress", builder.Configuration["EmailSettings:SenderAddress"] ?? "noreply@plandeck.app")
        .WithEnvironment("EmailSettings__PublicBaseUrl", planDeckServer.GetEndpoint("https"));

    if (isTestingPublishTarget)
    {
        planDeckServer.WithEnvironment("ASPNETCORE_ENVIRONMENT", "Testing");
    }

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
        .WithEnvironment("Authentication__Microsoft__ClientSecret", entraClientSecret)
        .WithEnvironment("Authentication__Microsoft__Required", bool.TrueString);

    planDeckServer
        .WaitFor(sqlDatabase)
        .PublishAsAzureContainerApp((infrastructure, app) =>
        {
            app.Configuration.Ingress.External = true;

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
        .WithEnvironment("EmailSettings__Host", "smtp")
        .WithEnvironment("EmailSettings__Port", "1025")
        .WithEnvironment("EmailSettings__SenderAddress", "noreply@plandeck.local")
        .WithEnvironment("EmailSettings__PublicBaseUrl", "https://localhost:7443")
        .WaitFor(sqlDatabase)
        .WaitFor(mailSmtp);

    if (builder.Configuration.GetValue<bool>("Testing:E2e:EnableMicrosoftAuthentication"))
    {
        planDeckServer
            .WithEnvironment("Authentication__Microsoft__TenantId", "e2e-tenant")
            .WithEnvironment("Authentication__Microsoft__ClientId", "e2e-client")
            .WithEnvironment("Authentication__Microsoft__ClientSecret", "e2e-secret")
            .WithEnvironment("Authentication__Microsoft__Required", bool.FalseString);
    }
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

internal sealed class AzureSqlPowerShellModuleWorkaround : InfrastructureResolver
{
    private const string SqlServerInstall =
        "Install-Module -Name SqlServer -RequiredVersion 22.3.0";

    public override IEnumerable<Provisionable> ResolveResources(
        IEnumerable<Provisionable> resources,
        ProvisioningBuildOptions options)
    {
        var resolvedResources = resources.ToArray();

        foreach (var script in resolvedResources.OfType<AzurePowerShellScript>())
        {
            if (script.ScriptContent.Value is not { } content
                || !content.Contains(SqlServerInstall, StringComparison.Ordinal))
            {
                continue;
            }

            // Temporary workaround for https://github.com/microsoft/aspire/issues/18845.
            var updatedContent = content.Replace(
                "-RequiredVersion 22.3.0",
                "-RequiredVersion 22.4.5.1",
                StringComparison.Ordinal);
            updatedContent = updatedContent.Replace(
                "Import-Module SqlServer",
                "Import-Module SqlServer -RequiredVersion 22.4.5.1 -Force",
                StringComparison.Ordinal);
            script.ScriptContent.Assign(new BicepValue<string>(updatedContent));
        }

        return base.ResolveResources(resolvedResources, options);
    }
}
