using Microsoft.EntityFrameworkCore;
using PlanDeck.Application.Abstractions;
using PlanDeck.Application.Domain;
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
        var repository = new TeamRepository(context, new FakeCurrentUserContext(TenantA, userId, authenticated: true));

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

        var teamsA = (await new TeamRepository(readA, new FakeCurrentUserContext(TenantA, Guid.Empty, authenticated: true))
            .GetTeamsAsync(CancellationToken.None)).Select(t => t.Name).ToList();
        var teamsB = (await new TeamRepository(readB, new FakeCurrentUserContext(TenantB, Guid.Empty, authenticated: true))
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
            var repository = new TeamRepository(addContext, new FakeCurrentUserContext(TenantA, Guid.NewGuid(), authenticated: true));
            await repository.AddMemberAsync(teamId, email, "A member", CancellationToken.None);
        }

        await using var readA = CreateContext(new FakeCurrentUserContext(TenantA, Guid.Empty, authenticated: true));
        await using var readB = CreateContext(new FakeCurrentUserContext(TenantB, Guid.Empty, authenticated: true));

        var membersA = await new TeamRepository(readA, new FakeCurrentUserContext(TenantA, Guid.Empty, authenticated: true))
            .GetMembersAsync(teamId, CancellationToken.None);
        var membersB = await new TeamRepository(readB, new FakeCurrentUserContext(TenantB, Guid.Empty, authenticated: true))
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
        var repository = new TeamRepository(context, new FakeCurrentUserContext(TenantA, Guid.NewGuid(), authenticated: true));

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
        var repository = new TeamRepository(context, new FakeCurrentUserContext(TenantA, Guid.NewGuid(), authenticated: true));

        var member = await repository.AddMemberAsync(teamId, email, null, CancellationToken.None);

        var removed = await repository.RemoveMemberAsync(teamId, member.Id, CancellationToken.None);
        var remaining = await repository.GetMembersAsync(teamId, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(removed, Is.True);
            Assert.That(remaining.Select(m => m.Email), Does.Not.Contain(email));
        });
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

    private static async Task<Guid> CreateTeamAsync(Guid tenantId, string name)
    {
        await using var context = CreateContext(new FakeCurrentUserContext(tenantId, Guid.NewGuid(), authenticated: true));
        var repository = new TeamRepository(context, new FakeCurrentUserContext(tenantId, Guid.NewGuid(), authenticated: true));
        var team = await repository.CreateTeamAsync(name, null, CancellationToken.None);
        return team.Id;
    }

    private static PlanDeckDbContext CreateContext(ICurrentUserContext currentUser)
    {
        var options = new DbContextOptionsBuilder<PlanDeckDbContext>()
            .UseSqlServer(AspireAppFixture.ConnectionString, sql => sql.EnableRetryOnFailure())
            .Options;

        return new PlanDeckDbContext(options, currentUser);
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
