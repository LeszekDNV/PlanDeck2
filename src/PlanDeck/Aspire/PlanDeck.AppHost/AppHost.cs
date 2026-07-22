using Azure.Provisioning.AppContainers;
using Azure.Provisioning.KeyVault;
using Azure.Provisioning.Sql;

var builder = DistributedApplication.CreateBuilder(args);

var planDeckServer = builder.AddProject<Projects.PlanDeck_Server>("plandeck-server")
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
    var entraTenantId = builder.AddParameter("entra-tenant-id");
    var entraClientId = builder.AddParameter("entra-client-id");
    var entraClientSecret = builder.AddParameter("entra-client-secret", secret: true);

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
        .WithEnvironment("Authentication__Microsoft__TenantId", entraTenantId)
        .WithEnvironment("Authentication__Microsoft__ClientId", entraClientId)
        .WithEnvironment("Authentication__Microsoft__ClientSecret", entraClientSecret)
        .WithEnvironment("EmailSettings__Host", "smtp")
        .WithEnvironment("EmailSettings__Port", "587")
        .WaitFor(sqlDatabase)
        .PublishAsAzureContainerApp((infrastructure, app) =>
        {
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

builder.Build().Run();
