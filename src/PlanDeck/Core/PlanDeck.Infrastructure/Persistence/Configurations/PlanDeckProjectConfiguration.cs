using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PlanDeck.Application.Domain;

namespace PlanDeck.Infrastructure.Persistence.Configurations;

public sealed class PlanDeckProjectConfiguration : IEntityTypeConfiguration<PlanDeckProject>
{
    public void Configure(EntityTypeBuilder<PlanDeckProject> builder)
    {
        builder.ToTable("Projects");
        builder.HasKey(project => project.Id);
        builder.Property(project => project.Id).ValueGeneratedNever();
        builder.Property(project => project.Name).IsRequired().HasMaxLength(200);
        builder.Property(project => project.Description).HasMaxLength(1024);
        builder.HasAlternateKey(project => new { project.TenantId, project.Id });
        builder.HasIndex(project => new { project.TenantId, project.Name }).IsUnique();
    }
}
