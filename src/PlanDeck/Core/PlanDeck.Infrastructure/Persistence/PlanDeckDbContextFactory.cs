using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using PlanDeck.Application.Abstractions;

namespace PlanDeck.Infrastructure.Persistence;

public sealed class PlanDeckDbContextFactory : IDesignTimeDbContextFactory<PlanDeckDbContext>
{
    public PlanDeckDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<PlanDeckDbContext>()
            .UseSqlServer("Server=(localdb)\\design-time;Database=PlanDeckDb;Trusted_Connection=True;")
            .Options;

        return new PlanDeckDbContext(options, new DesignTimeCurrentUserContext());
    }

    private sealed class DesignTimeCurrentUserContext : ICurrentUserContext
    {
        public Guid TenantId => Guid.Empty;

        public Guid UserId => Guid.Empty;

        public bool IsAuthenticated => false;
    }
}
