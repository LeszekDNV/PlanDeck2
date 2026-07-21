using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PlanDeck.Application.Domain;

namespace PlanDeck.Infrastructure.Persistence.Configurations;

public sealed class ProjectMemberConfiguration : IEntityTypeConfiguration<ProjectMember>
{
    public void Configure(EntityTypeBuilder<ProjectMember> builder)
    {
        builder.ToTable("ProjectMembers", table =>
        {
            table.HasCheckConstraint(
                "CK_ProjectMembers_Role",
                "[Role] IN (1, 2, 3)");
            table.HasCheckConstraint(
                "CK_ProjectMembers_Status",
                "[Status] IN (0, 1)");
            table.HasCheckConstraint(
                "CK_ProjectMembers_Resolution",
                "([Status] = 0 AND [AppUserId] IS NULL AND [AcceptedAtUtc] IS NULL) OR "
                + "([Status] = 1 AND [AppUserId] IS NOT NULL AND [AcceptedAtUtc] IS NOT NULL)");
        });

        builder.HasKey(member => member.Id);
        builder.Property(member => member.Id).ValueGeneratedNever();
        builder.Property(member => member.Email).IsRequired().HasMaxLength(320);
        builder.Property(member => member.NormalizedEmail).IsRequired().HasMaxLength(320);
        builder.Property(member => member.Role).HasConversion<int>();
        builder.Property(member => member.Status).HasConversion<int>();

        builder.HasIndex(member => new { member.TenantId, member.ProjectId, member.AppUserId })
            .IsUnique()
            .HasFilter("[AppUserId] IS NOT NULL");
        builder.HasIndex(member => new { member.TenantId, member.ProjectId, member.NormalizedEmail })
            .IsUnique()
            .HasFilter("[Status] = 0");
        builder.HasIndex(member => new { member.TenantId, member.ProjectId })
            .IsUnique()
            .HasFilter("[Role] = 3 AND [Status] = 1");

        builder.HasOne<PlanDeckProject>()
            .WithMany()
            .HasForeignKey(member => new { member.TenantId, member.ProjectId })
            .HasPrincipalKey(project => new { project.TenantId, project.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<AppUser>()
            .WithMany()
            .HasForeignKey(member => new { member.TenantId, member.AppUserId })
            .HasPrincipalKey(user => new { user.TenantId, user.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
