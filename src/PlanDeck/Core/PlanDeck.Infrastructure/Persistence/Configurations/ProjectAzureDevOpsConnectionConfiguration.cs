using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PlanDeck.Application.Domain;

namespace PlanDeck.Infrastructure.Persistence.Configurations;

public sealed class ProjectAzureDevOpsConnectionConfiguration
    : IEntityTypeConfiguration<ProjectAzureDevOpsConnection>
{
    public void Configure(EntityTypeBuilder<ProjectAzureDevOpsConnection> builder)
    {
        builder.ToTable("ProjectAzureDevOpsConnections", table =>
        {
            table.HasCheckConstraint(
                "CK_ProjectAzureDevOpsConnections_ValidationState",
                "[ValidationState] IN (0, 1, 2)");
        });
        builder.HasKey(connection => connection.Id);
        builder.Property(connection => connection.Id).ValueGeneratedNever();
        builder.Property(connection => connection.OrganizationUrl).IsRequired().HasMaxLength(512);
        builder.Property(connection => connection.AzureDevOpsProject).IsRequired().HasMaxLength(256);
        builder.Property(connection => connection.EstimateField).IsRequired().HasMaxLength(256);
        builder.Property(connection => connection.DescriptionField).IsRequired().HasMaxLength(256);
        builder.Property(connection => connection.ReproStepsField).IsRequired().HasMaxLength(256);
        builder.Property(connection => connection.AcceptanceCriteriaField).IsRequired().HasMaxLength(256);
        builder.Property(connection => connection.SecretName).IsRequired().HasMaxLength(127);
        builder.HasIndex(connection => new { connection.TenantId, connection.ProjectId }).IsUnique();
        builder.HasOne<PlanDeckProject>()
            .WithOne()
            .HasForeignKey<ProjectAzureDevOpsConnection>(
                connection => new { connection.TenantId, connection.ProjectId })
            .HasPrincipalKey<PlanDeckProject>(project => new { project.TenantId, project.Id })
            .OnDelete(DeleteBehavior.Cascade);
    }
}
