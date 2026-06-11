namespace PlanDeck.Server.Extensions;

public static class ServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddSqlDatabase(IConfiguration configuration)
        {
            return services;
        }

        public IServiceCollection AddExternalServices(IConfiguration configuration)
        {
            return services;
        }

        public IServiceCollection AddLocalServices()
        {
            services.AddHttpContextAccessor();
            return services;
        }
    }



    public static async Task<WebApplication> ApplyMigrationsAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();

        return app;
    }
}
