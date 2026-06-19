using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PlanDeck.Application.Domain;

namespace PlanDeck.Infrastructure.Persistence.Configurations;

public sealed class SessionTaskConfiguration : IEntityTypeConfiguration<SessionTask>
{
    public void Configure(EntityTypeBuilder<SessionTask> builder)
    {
        builder.ToTable("SessionTasks");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id)
            .ValueGeneratedNever();

        builder.Property(t => t.Title)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(t => t.Source)
            .HasConversion<int>();

        builder.Property(t => t.WorkItemType)
            .HasMaxLength(128);

        builder.Property(t => t.State)
            .HasMaxLength(128);

        builder.HasIndex(t => t.TenantId);

        builder.HasIndex(t => t.SessionId);

        builder.HasIndex(t => new { t.SessionId, t.AdoWorkItemId })
            .IsUnique()
            .HasFilter("[AdoWorkItemId] IS NOT NULL");
    }
}
