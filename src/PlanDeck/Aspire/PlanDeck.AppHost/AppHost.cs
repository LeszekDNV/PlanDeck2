var builder = DistributedApplication.CreateBuilder(args);

var sqlDatabase = builder.AddSqlServer("sql-server", port: 2140)
    .WithImage("mssql/server:2025-latest")
    .WithDataVolume()
    .WithLifetime(ContainerLifetime.Persistent)
    .AddDatabase("PlanDeckDb");

var mailSmtp = builder.AddMailPit("smtp", 1080, 1025);

builder.AddProject<Projects.PlanDeck_Server>("plandeck-server")
       .WithReference(sqlDatabase, "DefaultConnection")
       .WithEnvironment("EmailSettings:Host", "localhost")
       .WithEnvironment("EmailSettings:Port", "1025")
       .WaitFor(sqlDatabase)
       ;

builder.Build().Run();
