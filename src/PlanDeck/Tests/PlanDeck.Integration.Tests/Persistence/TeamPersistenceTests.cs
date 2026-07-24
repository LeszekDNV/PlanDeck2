using Microsoft.EntityFrameworkCore;
using PlanDeck.Application.Abstractions;
using PlanDeck.Application.Domain;
using PlanDeck.Infrastructure.Identity;
using PlanDeck.Infrastructure.Persistence;

namespace PlanDeck.Integration.Tests.Persistence;

[TestFixture]
public sealed class TeamPersistenceTests
{
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Test]
    public async Task CreateTeam_StampsTenantAuditAndCreatedBy()
    {
        var userId = Guid.NewGuid();
        var name = $"team-{Guid.NewGuid():N}";

        await using var context = CreateContext(new FakeCurrentUserContext(TenantA, userId, authenticated: true));
        var repository = new TeamRepository(context, new FakeCurrentUserContext(TenantA, userId, authenticated: true), IdentityRepository);

        var team = await repository.CreateTeamAsync(name, "A description", CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(team.Id, Is.Not.EqualTo(Guid.Empty));
            Assert.That(team.TenantId, Is.EqualTo(TenantA));
            Assert.That(team.CreatedByUserId, Is.EqualTo(userId));
            Assert.That(team.CreatedAtUtc, Is.Not.EqualTo(default(DateTimeOffset)));
            Assert.That(team.UpdatedAtUtc, Is.Not.EqualTo(default(DateTimeOffset)));
        });
    }

    [Test]
    public async Task Teams_AreScopedPerTenant_BothDirections()
    {
        var nameA = $"a-{Guid.NewGuid():N}";
        var nameB = $"b-{Guid.NewGuid():N}";

        await CreateTeamAsync(TenantA, nameA);
        await CreateTeamAsync(TenantB, nameB);

        await using var readA = CreateContext(new FakeCurrentUserContext(TenantA, Guid.Empty, authenticated: true));
        await using var readB = CreateContext(new FakeCurrentUserContext(TenantB, Guid.Empty, authenticated: true));

        var teamsA = (await new TeamRepository(readA, new FakeCurrentUserContext(TenantA, Guid.Empty, authenticated: true), IdentityRepository)
            .GetTeamsAsync(CancellationToken.None)).Select(t => t.Name).ToList();
        var teamsB = (await new TeamRepository(readB, new FakeCurrentUserContext(TenantB, Guid.Empty, authenticated: true), IdentityRepository)
            .GetTeamsAsync(CancellationToken.None)).Select(t => t.Name).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(teamsA, Does.Contain(nameA));
            Assert.That(teamsA, Does.Not.Contain(nameB));
            Assert.That(teamsB, Does.Contain(nameB));
            Assert.That(teamsB, Does.Not.Contain(nameA));
        });
    }

    [Test]
    public async Task Members_AreScopedPerTenant()
    {
        var email = $"member-{Guid.NewGuid():N}@example.com";

        var teamId = await CreateTeamAsync(TenantA, $"team-{Guid.NewGuid():N}");

        await using (var addContext = CreateContext(new FakeCurrentUserContext(TenantA, Guid.NewGuid(), authenticated: true)))
        {
            var repository = new TeamRepository(addContext, new FakeCurrentUserContext(TenantA, Guid.NewGuid(), authenticated: true), IdentityRepository);
            await repository.AddMemberAsync(teamId, email, "A member", CancellationToken.None);
        }

        await using var readA = CreateContext(new FakeCurrentUserContext(TenantA, Guid.Empty, authenticated: true));
        await using var readB = CreateContext(new FakeCurrentUserContext(TenantB, Guid.Empty, authenticated: true));

        var membersA = await new TeamRepository(readA, new FakeCurrentUserContext(TenantA, Guid.Empty, authenticated: true), IdentityRepository)
            .GetMembersAsync(teamId, CancellationToken.None);
        var membersB = await new TeamRepository(readB, new FakeCurrentUserContext(TenantB, Guid.Empty, authenticated: true), IdentityRepository)
            .GetMembersAsync(teamId, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(membersA.Select(m => m.Email), Does.Contain(email));
            Assert.That(membersB, Is.Empty);
        });
    }

    [Test]
    public async Task AddMember_DuplicateEmail_Throws()
    {
        var email = $"dupe-{Guid.NewGuid():N}@example.com";
        var teamId = await CreateTeamAsync(TenantA, $"team-{Guid.NewGuid():N}");

        await using var context = CreateContext(new FakeCurrentUserContext(TenantA, Guid.NewGuid(), authenticated: true));
        var repository = new TeamRepository(context, new FakeCurrentUserContext(TenantA, Guid.NewGuid(), authenticated: true), IdentityRepository);

        await repository.AddMemberAsync(teamId, email, null, CancellationToken.None);

        Assert.That(
            async () => await repository.AddMemberAsync(teamId, email, null, CancellationToken.None),
            Throws.TypeOf<DuplicateTeamMemberException>());
    }

    [Test]
    public async Task RemoveMember_RemovesTheMember()
    {
        var email = $"remove-{Guid.NewGuid():N}@example.com";
        var teamId = await CreateTeamAsync(TenantA, $"team-{Guid.NewGuid():N}");

        await using var context = CreateContext(new FakeCurrentUserContext(TenantA, Guid.NewGuid(), authenticated: true));
        var repository = new TeamRepository(context, new FakeCurrentUserContext(TenantA, Guid.NewGuid(), authenticated: true), IdentityRepository);

        var member = await repository.AddMemberAsync(teamId, email, null, CancellationToken.None);

        var removed = await repository.RemoveMemberAsync(teamId, member.Member.Id, CancellationToken.None);
        var remaining = await repository.GetMembersAsync(teamId, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(removed, Is.True);
            Assert.That(remaining.Select(m => m.Email), Does.Not.Contain(email));
        });
    }

    [Test]
    public async Task DeleteTeam_ByCreator_DeletesTeamAndMembers()
    {
        var creatorId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var teamId = await CreateTeamAsync(TenantA, $"team-{Guid.NewGuid():N}", creatorId);

        await using (var seed = CreateContext(new FakeCurrentUserContext(TenantA, creatorId, authenticated: true)))
        {
            var memberEmail = AddUser(seed, TenantA, memberId, $"member-{Guid.NewGuid():N}@example.com", emailConfirmed: true);
            await seed.SaveChangesAsync();

            var repository = new TeamRepository(seed, new FakeCurrentUserContext(TenantA, creatorId, authenticated: true), IdentityRepo(seed));
            await repository.AddMemberAsync(teamId, memberEmail, null, CancellationToken.None);
        }

        await using (var delete = CreateContext(new FakeCurrentUserContext(TenantA, creatorId, authenticated: true)))
        {
            var repository = new TeamRepository(delete, new FakeCurrentUserContext(TenantA, creatorId, authenticated: true), IdentityRepo(delete));
            var result = await repository.DeleteTeamAsync(teamId, CancellationToken.None);
            Assert.That(result, Is.EqualTo(DeleteTeamResult.Deleted));
        }

        await using var read = CreateContext(new FakeCurrentUserContext(TenantA, Guid.Empty, authenticated: true));
        var teamExists = await read.Teams.AnyAsync(t => t.Id == teamId);
        var membersExist = await read.TeamMembers.AnyAsync(m => m.TeamId == teamId);
        Assert.That(teamExists, Is.False);
        Assert.That(membersExist, Is.False);
    }

    [Test]
    public async Task DeleteTeam_ByMember_ReturnsForbidden()
    {
        var creatorId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var teamId = await CreateTeamAsync(TenantA, $"team-{Guid.NewGuid():N}", creatorId);

        await using (var seed = CreateContext(new FakeCurrentUserContext(TenantA, creatorId, authenticated: true)))
        {
            var memberEmail = AddUser(seed, TenantA, memberId, $"member-{Guid.NewGuid():N}@example.com", emailConfirmed: true);
            await seed.SaveChangesAsync();

            var repository = new TeamRepository(seed, new FakeCurrentUserContext(TenantA, creatorId, authenticated: true), IdentityRepo(seed));
            await repository.AddMemberAsync(teamId, memberEmail, null, CancellationToken.None);
        }

        await using var delete = CreateContext(new FakeCurrentUserContext(TenantA, memberId, authenticated: true));
        var memberRepository = new TeamRepository(delete, new FakeCurrentUserContext(TenantA, memberId, authenticated: true), IdentityRepo(delete));
        var result = await memberRepository.DeleteTeamAsync(teamId, CancellationToken.None);

        Assert.That(result, Is.EqualTo(DeleteTeamResult.Forbidden));
    }

    [Test]
    public async Task DeleteTeam_CrossTenant_ReturnsNotFound()
    {
        var creatorId = Guid.NewGuid();
        var teamId = await CreateTeamAsync(TenantA, $"team-{Guid.NewGuid():N}");

        await using var delete = CreateContext(new FakeCurrentUserContext(TenantB, creatorId, authenticated: true));
        var repository = new TeamRepository(delete, new FakeCurrentUserContext(TenantB, creatorId, authenticated: true), IdentityRepo(delete));
        var result = await repository.DeleteTeamAsync(teamId, CancellationToken.None);

        Assert.That(result, Is.EqualTo(DeleteTeamResult.NotFound));
    }

    [Test]
    public async Task InviteMember_UnknownEmail_CreatesPendingMembershipAndInvitation()
    {
        var creatorId = Guid.NewGuid();
        var teamId = await CreateTeamAsync(TenantA, $"team-{Guid.NewGuid():N}");
        var invitedEmail = $"invite-{Guid.NewGuid():N}@example.com";

        await using var context = CreateContext(new FakeCurrentUserContext(TenantA, creatorId, authenticated: true));
        var repository = new TeamRepository(context, new FakeCurrentUserContext(TenantA, creatorId, authenticated: true), IdentityRepo(context));
        var result = await repository.AddMemberAsync(teamId, invitedEmail, "Invitee", CancellationToken.None);

        var normalizedEmail = invitedEmail.ToUpperInvariant();
        var invitation = await context.TenantInvitations
            .AsNoTracking()
            .SingleOrDefaultAsync(i => i.NormalizedEmail == normalizedEmail);

        Assert.Multiple(() =>
        {
            Assert.That(result.Member.Status, Is.EqualTo(InvitationStatus.Pending));
            Assert.That(result.Member.AppUserId, Is.Null);
            Assert.That(result.InvitationToken, Is.Not.Null.And.Not.Empty);
            Assert.That(invitation, Is.Not.Null);
            Assert.That(invitation!.Status, Is.EqualTo(InvitationStatus.Pending));
        });
    }

    [Test]
    public async Task InviteMember_ExistingSameTenantConfirmedUser_AcceptedImmediately()
    {
        var creatorId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var teamId = await CreateTeamAsync(TenantA, $"team-{Guid.NewGuid():N}", creatorId);

        await using var context = CreateContext(new FakeCurrentUserContext(TenantA, creatorId, authenticated: true));
        var memberEmail = AddUser(context, TenantA, memberId, $"member-{Guid.NewGuid():N}@example.com", emailConfirmed: true);
        await context.SaveChangesAsync();

        var repository = new TeamRepository(context, new FakeCurrentUserContext(TenantA, creatorId, authenticated: true), IdentityRepo(context));
        var result = await repository.AddMemberAsync(teamId, memberEmail, null, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Member.Status, Is.EqualTo(InvitationStatus.Accepted));
            Assert.That(result.Member.AppUserId, Is.EqualTo(memberId));
            Assert.That(result.InvitationToken, Is.Null);
        });
    }

    [Test]
    public async Task InviteMember_ExistingDifferentTenantConfirmedUser_ThrowsAccountTenantConflict()
    {
        var creatorId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var memberEmail = $"member-{Guid.NewGuid():N}@example.com";
        var teamId = await CreateTeamAsync(TenantA, $"team-{Guid.NewGuid():N}", creatorId);

        await using (var otherTenant = CreateContext(new FakeCurrentUserContext(TenantB, memberId, authenticated: true)))
        {
            AddUser(otherTenant, TenantB, memberId, memberEmail, emailConfirmed: true);
            await otherTenant.SaveChangesAsync();
        }

        await using var context = CreateContext(new FakeCurrentUserContext(TenantA, creatorId, authenticated: true));
        var repository = new TeamRepository(context, new FakeCurrentUserContext(TenantA, creatorId, authenticated: true), IdentityRepo(context));

        Assert.That(
            async () => await repository.AddMemberAsync(teamId, MakeUniqueEmail(memberId, memberEmail), null, CancellationToken.None),
            Throws.TypeOf<AccountTenantConflictException>());
    }

    [Test]
    public void CreateTeam_WithNoTenantContext_IsRejectedFailClosed()
    {
        using var context = CreateContext(new FakeCurrentUserContext(Guid.Empty, Guid.Empty, authenticated: false));
        context.Teams.Add(new Team { Name = $"no-tenant-{Guid.NewGuid():N}" });

        Assert.That(() => context.SaveChanges(), Throws.TypeOf<InvalidOperationException>());
    }

    [Test]
    public void Update_AttachedCrossTenantTeam_IsRejected()
    {
        using var context = CreateContext(new FakeCurrentUserContext(TenantA, Guid.Empty, authenticated: true));
        context.Teams.Update(new Team
        {
            Id = Guid.NewGuid(),
            Name = $"belongs-to-b-{Guid.NewGuid():N}",
            TenantId = TenantB,
        });

        Assert.That(() => context.SaveChanges(), Throws.TypeOf<InvalidOperationException>());
    }

    private static async Task<Guid> CreateTeamAsync(Guid tenantId, string name, Guid? createdByUserId = null)
    {
        var userId = createdByUserId ?? Guid.NewGuid();
        await using var context = CreateContext(new FakeCurrentUserContext(tenantId, userId, authenticated: true));
        await EnsureTenantAsync(context, tenantId);
        var repository = new TeamRepository(context, new FakeCurrentUserContext(tenantId, userId, authenticated: true), IdentityRepository);
        var team = await repository.CreateTeamAsync(name, null, CancellationToken.None);
        return team.Id;
    }

    private static async Task EnsureTenantAsync(PlanDeckDbContext db, Guid tenantId)
    {
        var exists = await db.Tenants.AnyAsync(t => t.Id == tenantId);
        if (!exists)
        {
            db.Tenants.Add(new PlanDeckTenant
            {
                Id = tenantId,
                Name = $"Test tenant {tenantId:N}",
                CreatedAtUtc = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }
    }

    private static PlanDeckDbContext CreateContext(ICurrentUserContext currentUser)
    {
        var options = new DbContextOptionsBuilder<PlanDeckDbContext>()
            .UseSqlServer(AspireAppFixture.ConnectionString, sql => sql.EnableRetryOnFailure())
            .Options;

        return new PlanDeckDbContext(options, currentUser);
    }

    private static IIdentityAccountRepository IdentityRepository => new FakeIdentityAccountRepository();

    private static IdentityAccountRepository IdentityRepo(PlanDeckDbContext db) => new(db);

    private static string AddUser(
        PlanDeckDbContext db,
        Guid tenantId,
        Guid userId,
        string email,
        bool emailConfirmed = false)
    {
        var unique = MakeUniqueEmail(userId, email);
        db.Users.Add(new ApplicationUser
        {
            Id = userId,
            UserName = unique,
            Email = unique,
            NormalizedEmail = unique.ToUpperInvariant(),
            NormalizedUserName = unique.ToUpperInvariant(),
            EmailConfirmed = emailConfirmed
        });
        db.AppUsers.Add(new AppUser
        {
            Id = userId,
            TenantId = tenantId,
            FirstName = "Test",
            LastName = "User",
            Role = TenantRole.Member,
            IsActive = true
        });
        return unique;
    }

    private static string MakeUniqueEmail(Guid id, string email)
    {
        var at = email.IndexOf('@', StringComparison.Ordinal);
        return at < 0
            ? $"{email}-{id:N}@example.com"
            : $"{email[..at]}-{id:N}{email[at..]}";
    }

    private sealed class FakeIdentityAccountRepository : IIdentityAccountRepository
    {
        public Task<IdentityAccount?> FindByNormalizedEmailAsync(
            string normalizedEmail,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IdentityAccount?>(null);

        public Task<IdentityAccount?> FindByNormalizedUserNameAsync(
            string normalizedUserName,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IdentityAccount?>(null);

        public Task<IdentityAccount?> FindByIdAsync(
            Guid id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IdentityAccount?>(null);
    }

    private sealed class FakeCurrentUserContext(Guid tenantId, Guid userId, bool authenticated) : ICurrentUserContext
    {
        public Guid TenantId { get; } = tenantId;

        public Guid UserId { get; } = userId;

        public bool IsAuthenticated { get; } = authenticated;

        public string? DisplayName { get; }

        public string? Email { get; }
    }
}
