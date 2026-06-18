using Microsoft.EntityFrameworkCore;

namespace PlanDeck.Infrastructure.Persistence;

public sealed class PlanDeckDbContext(DbContextOptions<PlanDeckDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PlanDeckDbContext).Assembly);
    }
}
