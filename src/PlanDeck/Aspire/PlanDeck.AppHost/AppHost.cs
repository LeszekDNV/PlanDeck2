var builder = DistributedApplication.CreateBuilder(args);

var planDeckServer = builder.AddProject<Projects.PlanDeck_Server>("plandeck-server")
    .WithExternalHttpEndpoints();

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

builder.Build().Run();
