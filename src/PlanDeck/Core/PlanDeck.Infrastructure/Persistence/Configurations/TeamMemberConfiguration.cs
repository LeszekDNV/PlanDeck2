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

        builder.HasIndex(m => m.TenantId);

        builder.HasIndex(m => new { m.TenantId, m.TeamId, m.Email })
            .IsUnique();
    }
}
