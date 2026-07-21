using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PlanDeck.Application.Domain;

namespace PlanDeck.Infrastructure.Persistence.Configurations;

public sealed class ProjectTeamConfiguration : IEntityTypeConfiguration<ProjectTeam>
{
    public void Configure(EntityTypeBuilder<ProjectTeam> builder)
    {
        builder.ToTable("ProjectTeams");
        builder.HasKey(assignment => assignment.Id);
        builder.Property(assignment => assignment.Id).ValueGeneratedNever();
        builder.HasIndex(assignment => new
        {
            assignment.TenantId,
            assignment.ProjectId,
            assignment.TeamId
        }).IsUnique();

        builder.HasOne<PlanDeckProject>()
            .WithMany()
            .HasForeignKey(assignment => new { assignment.TenantId, assignment.ProjectId })
            .HasPrincipalKey(project => new { project.TenantId, project.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Team>()
            .WithMany()
            .HasForeignKey(assignment => new { assignment.TenantId, assignment.TeamId })
            .HasPrincipalKey(team => new { team.TenantId, team.Id })
            .OnDelete(DeleteBehavior.Cascade);
    }
}
