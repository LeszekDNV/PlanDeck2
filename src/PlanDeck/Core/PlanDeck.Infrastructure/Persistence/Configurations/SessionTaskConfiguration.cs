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

        builder.Property(t => t.AgreedEstimate)
            .HasMaxLength(32);

        builder.HasIndex(t => new { t.SessionId, t.AdoWorkItemId })
            .IsUnique()
            .HasFilter("[AdoWorkItemId] IS NOT NULL");

        builder.HasOne<PlanningSession>()
            .WithMany(session => session.Tasks)
            .HasForeignKey(task => new { task.TenantId, task.SessionId })
            .HasPrincipalKey(session => new { session.TenantId, session.Id })
            .OnDelete(DeleteBehavior.Cascade);
    }
}
