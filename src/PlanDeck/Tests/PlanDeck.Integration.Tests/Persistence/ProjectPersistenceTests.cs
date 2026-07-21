using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PlanDeck.Application.Abstractions;
using PlanDeck.Application.Domain;
using PlanDeck.Infrastructure.Persistence;

namespace PlanDeck.Integration.Tests.Persistence;

[TestFixture]
public sealed class ProjectPersistenceTests
{
    [Test]
    public async Task CreateAndTransferOwnership_MaintainsExactlyOneOwner()
    {
        var tenantId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var newOwnerId = Guid.NewGuid();
        var owner = User(ownerId, "owner@example.com");
        var newOwner = User(newOwnerId, "new-owner@example.com");

        await using var db = CreateContext(CurrentUser(tenantId, ownerId, owner.Email));
        db.AppUsers.AddRange(owner, newOwner);
        await db.SaveChangesAsync();

        var repository = new ProjectRepository(db, CurrentUser(tenantId, ownerId, owner.Email));
        var project = await repository.CreateAsync(
            "Ownership project",
            null,
            owner.Email,
            CancellationToken.None);
        var newOwnerMembership = new ProjectMember
        {
            ProjectId = project.Id,
            AppUserId = newOwnerId,
            Email = newOwner.Email,
            Role = ProjectRole.Member,
            Status = InvitationStatus.Accepted,
            InvitedByUserId = ownerId,
            AcceptedAtUtc = DateTimeOffset.UtcNow
        };
        db.ProjectMembers.Add(newOwnerMembership);
        await db.SaveChangesAsync();

        await repository.TransferOwnershipAsync(
            project.Id,
            newOwnerMembership.Id,
            CancellationToken.None);

        var memberships = await db.ProjectMembers
            .Where(member => member.ProjectId == project.Id)
            .ToListAsync();
        Assert.Multiple(() =>
        {
            Assert.That(
                memberships.Count(member => member.Role == ProjectRole.Owner),
                Is.EqualTo(1));
            Assert.That(
                memberships.Single(member => member.AppUserId == ownerId).Role,
                Is.EqualTo(ProjectRole.Admin));
            Assert.That(
                memberships.Single(member => member.AppUserId == newOwnerId).Role,
                Is.EqualTo(ProjectRole.Owner));
        });
    }

    [Test]
    public async Task EffectiveRole_DirectRoleWinsThenFallsBackToTeamMember()
    {
        var tenantId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var owner = User(ownerId, "owner@example.com");
        var member = User(memberId, "member@example.com");

        await using var db = CreateContext(CurrentUser(tenantId, ownerId, owner.Email));
        db.AppUsers.AddRange(owner, member);
        var project = new PlanDeckProject
        {
            Name = $"project-{Guid.NewGuid():N}",
            CreatedByUserId = ownerId
        };
        var team = new Team
        {
            Name = $"team-{Guid.NewGuid():N}",
            CreatedByUserId = ownerId
        };
        db.AddRange(project, team);
        db.ProjectMembers.Add(new ProjectMember
        {
            ProjectId = project.Id,
            AppUserId = memberId,
            Email = member.Email,
            Role = ProjectRole.Admin,
            Status = InvitationStatus.Accepted,
            InvitedByUserId = ownerId,
            AcceptedAtUtc = DateTimeOffset.UtcNow
        });
        db.TeamMembers.Add(new TeamMember
        {
            TeamId = team.Id,
            AppUserId = memberId,
            Email = member.Email,
            Status = InvitationStatus.Accepted,
            InvitedByUserId = ownerId,
            AcceptedAtUtc = DateTimeOffset.UtcNow
        });
        db.ProjectTeams.Add(new ProjectTeam
        {
            ProjectId = project.Id,
            TeamId = team.Id,
            AssignedByUserId = ownerId
        });
        await db.SaveChangesAsync();

        await using var memberDb = CreateContext(CurrentUser(tenantId, memberId, member.Email));
        var resolver = new ProjectAccessResolver(
            memberDb,
            CurrentUser(tenantId, memberId, member.Email));
        Assert.That(
            await resolver.GetEffectiveRoleAsync(project.Id, CancellationToken.None),
            Is.EqualTo(ProjectRole.Admin));

        var directMembership = await memberDb.ProjectMembers.SingleAsync(candidate =>
            candidate.ProjectId == project.Id && candidate.AppUserId == memberId);
        memberDb.ProjectMembers.Remove(directMembership);
        await memberDb.SaveChangesAsync();

        Assert.That(
            await resolver.GetEffectiveRoleAsync(project.Id, CancellationToken.None),
            Is.EqualTo(ProjectRole.Member));
    }

    [Test]
    public async Task PendingInvitation_IsActivatedByMatchingProvisionedUser()
    {
        var tenantId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var owner = User(ownerId, "owner@example.com");
        var invitedEmail = $"invite-{Guid.NewGuid():N}@example.com";

        await using var db = CreateContext(CurrentUser(tenantId, ownerId, owner.Email));
        db.AppUsers.Add(owner);
        await db.SaveChangesAsync();
        var projects = new ProjectRepository(db, CurrentUser(tenantId, ownerId, owner.Email));
        var project = await projects.CreateAsync(
            "Invitations project",
            null,
            owner.Email,
            CancellationToken.None);
        var invitation = await projects.InviteMemberAsync(
            project.Id,
            invitedEmail,
            ProjectRole.Member,
            CancellationToken.None);
        Assert.That(invitation.Status, Is.EqualTo(InvitationStatus.Pending));
        var unresolvedUserId = Guid.NewGuid();
        var unresolvedAccess = new ProjectAccessResolver(
            db,
            CurrentUser(tenantId, unresolvedUserId, invitedEmail));
        Assert.That(
            await unresolvedAccess.GetEffectiveRoleAsync(project.Id, CancellationToken.None),
            Is.Null);

        var provisioned = await new AppUserRepository(db).UpsertAsync(
            tenantId,
            Guid.NewGuid(),
            "Invited user",
            invitedEmail.ToUpperInvariant(),
            CancellationToken.None);

        var activated = await db.ProjectMembers.SingleAsync(member => member.Id == invitation.Id);
        Assert.Multiple(() =>
        {
            Assert.That(activated.Status, Is.EqualTo(InvitationStatus.Accepted));
            Assert.That(activated.AppUserId, Is.EqualTo(provisioned.Id));
            Assert.That(activated.AcceptedAtUtc, Is.Not.Null);
        });

        var resolvedAccess = new ProjectAccessResolver(
            db,
            CurrentUser(tenantId, provisioned.Id, invitedEmail));
        Assert.That(
            await resolvedAccess.GetEffectiveRoleAsync(project.Id, CancellationToken.None),
            Is.EqualTo(ProjectRole.Member));
    }

    [Test]
    public async Task ProjectTeam_CannotLinkResourcesAcrossTenants()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var userId = Guid.NewGuid();
        Guid projectId;
        Guid teamId;

        await using (var dbA = CreateContext(CurrentUser(tenantA, userId, "a@example.com")))
        {
            var project = new PlanDeckProject
            {
                Name = $"project-{Guid.NewGuid():N}",
                CreatedByUserId = userId
            };
            dbA.Projects.Add(project);
            await dbA.SaveChangesAsync();
            projectId = project.Id;
        }

        await using (var dbB = CreateContext(CurrentUser(tenantB, userId, "b@example.com")))
        {
            var team = new Team
            {
                Name = $"team-{Guid.NewGuid():N}",
                CreatedByUserId = userId
            };
            dbB.Teams.Add(team);
            await dbB.SaveChangesAsync();
            teamId = team.Id;
        }

        await using var invalid = CreateContext(CurrentUser(tenantA, userId, "a@example.com"));
        invalid.ProjectTeams.Add(new ProjectTeam
        {
            ProjectId = projectId,
            TeamId = teamId,
            AssignedByUserId = userId
        });

        Assert.That(
            async () => await invalid.SaveChangesAsync(),
            Throws.TypeOf<DbUpdateException>());
    }

    [Test]
    public async Task CurrentSchema_RequiresProjectIdAndContainsNoTeamId()
    {
        await using var connection = new SqlConnection(AspireAppFixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT [COLUMN_NAME], [IS_NULLABLE]
            FROM [INFORMATION_SCHEMA].[COLUMNS]
            WHERE [TABLE_NAME] = 'Sessions'
              AND [COLUMN_NAME] IN ('ProjectId', 'TeamId');
            """;

        var columns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(0), reader.GetString(1));
        }

        Assert.Multiple(() =>
        {
            Assert.That(columns, Does.ContainKey("ProjectId"));
            Assert.That(columns["ProjectId"], Is.EqualTo("NO"));
            Assert.That(columns, Does.Not.ContainKey("TeamId"));
        });
    }

    [Test]
    public void SessionWithoutExistingProject_IsRejectedByForeignKey()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        using var db = CreateContext(CurrentUser(tenantId, userId, "user@example.com"));
        db.Sessions.Add(new PlanningSession
        {
            Name = $"orphan-{Guid.NewGuid():N}",
            ProjectId = Guid.NewGuid(),
            CreatedByUserId = userId
        });

        Assert.That(
            async () => await db.SaveChangesAsync(),
            Throws.TypeOf<DbUpdateException>());
    }

    private static AppUser User(Guid id, string email) => new()
    {
        Id = id,
        EntraObjectId = Guid.NewGuid(),
        DisplayName = email,
        Email = email
    };

    private static FakeCurrentUserContext CurrentUser(
        Guid tenantId,
        Guid userId,
        string email) =>
        new(tenantId, userId, email);

    private static PlanDeckDbContext CreateContext(ICurrentUserContext currentUser)
    {
        var options = new DbContextOptionsBuilder<PlanDeckDbContext>()
            .UseSqlServer(
                AspireAppFixture.ConnectionString,
                sql => sql.EnableRetryOnFailure())
            .Options;
        return new PlanDeckDbContext(options, currentUser);
    }

    private sealed class FakeCurrentUserContext(
        Guid tenantId,
        Guid userId,
        string email) : ICurrentUserContext
    {
        public Guid TenantId { get; } = tenantId;

        public Guid UserId { get; } = userId;

        public bool IsAuthenticated => true;

        public string? DisplayName => email;

        public string? Email => email;
    }
}
