using PlanDeck.Application.Domain;
using PlanDeck.Infrastructure.Persistence;

namespace PlanDeck.Integration.Tests;

internal static class PersistenceTestData
{
    public static Guid AddProject(PlanDeckDbContext db, Guid createdByUserId)
    {
        var project = new PlanDeckProject
        {
            Name = $"project-{Guid.NewGuid():N}",
            CreatedByUserId = createdByUserId
        };
        db.Projects.Add(project);
        return project.Id;
    }
}
