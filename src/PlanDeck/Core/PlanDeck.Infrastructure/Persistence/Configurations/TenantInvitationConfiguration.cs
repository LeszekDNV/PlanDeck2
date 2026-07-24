using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PlanDeck.Application.Domain;

namespace PlanDeck.Infrastructure.Persistence.Configurations;

public sealed class TenantInvitationConfiguration : IEntityTypeConfiguration<TenantInvitation>
{
    public void Configure(EntityTypeBuilder<TenantInvitation> builder)
    {
        builder.ToTable("TenantInvitations");

        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).ValueGeneratedNever();

        builder.Property(i => i.TokenHash)
            .IsRequired();

        builder.Property(i => i.NormalizedEmail)
            .IsRequired()
            .HasMaxLength(320);

        builder.Property(i => i.Role)
            .HasConversion<int>();

        builder.Property(i => i.Status)
            .HasConversion<int>();

        builder.Property(i => i.ExpiresAtUtc)
            .IsRequired();

        builder.HasIndex(i => new { i.TenantId, i.NormalizedEmail });

        builder.HasIndex(i => new { i.TenantId, i.TokenHash })
            .IsUnique()
            .HasFilter("[Status] = 0");

        builder.HasOne<PlanDeckTenant>()
            .WithMany()
            .HasForeignKey(i => i.TenantId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

