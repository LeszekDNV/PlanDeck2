using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PlanDeck.Application.Domain;

namespace PlanDeck.Infrastructure.Persistence.Configurations;

public sealed class PlanningSessionConfiguration : IEntityTypeConfiguration<PlanningSession>
{
    public void Configure(EntityTypeBuilder<PlanningSession> builder)
    {
        builder.ToTable("Sessions");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id)
            .ValueGeneratedNever();

        builder.Property(s => s.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(s => s.Status)
            .HasConversion<int>();

        builder.Property(s => s.ScaleType)
            .HasConversion<int>();

        builder.PrimitiveCollection(s => s.ScaleValues);

        builder.Property(s => s.ShareCode)
            .HasMaxLength(16);

        builder.HasIndex(s => s.TenantId);

        // Filtered unique index: share codes are tenant-agnostic join keys, so they must be
        // globally unique, but only across sessions that actually have one (Draft sessions are null).
        builder.HasIndex(s => s.ShareCode)
            .IsUnique()
            .HasFilter("[ShareCode] IS NOT NULL");

        builder.HasMany(s => s.Tasks)
            .WithOne()
            .HasForeignKey(t => t.SessionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
