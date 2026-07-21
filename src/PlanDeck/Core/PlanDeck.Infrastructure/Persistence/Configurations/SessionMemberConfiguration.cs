using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PlanDeck.Application.Domain;

namespace PlanDeck.Infrastructure.Persistence.Configurations;

public sealed class SessionMemberConfiguration : IEntityTypeConfiguration<SessionMember>
{
    public void Configure(EntityTypeBuilder<SessionMember> builder)
    {
        builder.ToTable("SessionMembers");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id)
            .ValueGeneratedNever();

        builder.Property(m => m.Email)
            .IsRequired()
            .HasMaxLength(320);

        builder.Property(m => m.DisplayName)
            .HasMaxLength(256);

        builder.HasIndex(m => new { m.TenantId, m.SessionId, m.Email })
            .IsUnique();

        builder.HasOne<PlanningSession>()
            .WithMany()
            .HasForeignKey(m => new { m.TenantId, m.SessionId })
            .HasPrincipalKey(session => new { session.TenantId, session.Id })
            .OnDelete(DeleteBehavior.Cascade);
    }
}
