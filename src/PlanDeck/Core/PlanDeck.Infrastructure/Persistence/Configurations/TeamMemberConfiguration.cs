using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PlanDeck.Application.Domain;

namespace PlanDeck.Infrastructure.Persistence.Configurations;

public sealed class TeamMemberConfiguration : IEntityTypeConfiguration<TeamMember>
{
    public void Configure(EntityTypeBuilder<TeamMember> builder)
    {
        builder.ToTable("TeamMembers");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id)
            .ValueGeneratedNever();

        builder.Property(m => m.Email)
            .IsRequired()
            .HasMaxLength(320);

        builder.Property(m => m.DisplayName)
            .HasMaxLength(256);

        builder.Property(m => m.NormalizedEmail)
            .IsRequired()
            .HasMaxLength(320);

        builder.Property(m => m.Status)
            .HasConversion<int>();

        builder.HasIndex(m => new { m.TenantId, m.TeamId, m.AppUserId })
            .IsUnique()
            .HasFilter("[AppUserId] IS NOT NULL");

        builder.HasIndex(m => new { m.TenantId, m.TeamId, m.NormalizedEmail })
            .IsUnique()
            .HasFilter("[Status] = 0");

        builder.HasOne<Team>()
            .WithMany()
            .HasForeignKey(m => new { m.TenantId, m.TeamId })
            .HasPrincipalKey(t => new { t.TenantId, t.Id })
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<AppUser>()
            .WithMany()
            .HasForeignKey(m => new { m.TenantId, m.AppUserId })
            .HasPrincipalKey(u => new { u.TenantId, u.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
