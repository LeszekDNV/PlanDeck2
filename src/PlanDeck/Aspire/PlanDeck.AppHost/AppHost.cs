var builder = DistributedApplication.CreateBuilder(args);

var planDeckServer = builder.AddProject<Projects.PlanDeck_Server>("plandeck-server")
    .WithExternalHttpEndpoints()
    .WithEndpoint("https", endpoint => endpoint.Port = 7443);

if (builder.ExecutionContext.IsPublishMode)
{
    _ = builder.AddAzureContainerAppEnvironment("aca-env");

    var sqlDatabase = builder.AddAzureSqlServer("sql-server")
        .AddDatabase("PlanDeckDb");

    var keyVault = builder.AddAzureKeyVault("key-vault");

    planDeckServer
        .WithReference(sqlDatabase, "DefaultConnection")
        .WithReference(keyVault)
        .WithEnvironment("EmailSettings__Host", "smtp")
        .WithEnvironment("EmailSettings__Port", "587")
        .WaitFor(sqlDatabase);
}
else
{
    var sqlDatabase = builder.AddSqlServer("sql-server", port: 2140)
        .WithImage("mssql/server:2025-latest")
        .WithDataVolume()
        .WithLifetime(ContainerLifetime.Persistent)
        .AddDatabase("PlanDeckDb");

    var mailSmtp = builder.AddMailPit("smtp", 1080, 1025);

    planDeckServer
        .WithReference(sqlDatabase, "DefaultConnection")
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
