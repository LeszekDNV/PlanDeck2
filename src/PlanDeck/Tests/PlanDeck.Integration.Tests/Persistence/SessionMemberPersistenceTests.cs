using Microsoft.EntityFrameworkCore;
using PlanDeck.Application.Abstractions;
using PlanDeck.Application.Domain;
using PlanDeck.Infrastructure.Persistence;

namespace PlanDeck.Integration.Tests.Persistence;

[TestFixture]
public sealed class SessionMemberPersistenceTests
{
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Test]
    public async Task AssignMember_StampsTenantAuditAndAssignedBy()
    {
        var userId = Guid.NewGuid();
        var email = $"member-{Guid.NewGuid():N}@example.com";
        var sessionId = await CreateSessionAsync(TenantA);

        await using var context = CreateContext(new FakeCurrentUserContext(TenantA, userId, authenticated: true));
        var repository = new SessionMemberRepository(context, new FakeCurrentUserContext(TenantA, userId, authenticated: true));

        var member = await repository.AssignMemberAsync(sessionId, email, "A member", CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(member.Id, Is.Not.EqualTo(Guid.Empty));
            Assert.That(member.TenantId, Is.EqualTo(TenantA));
            Assert.That(member.AssignedByUserId, Is.EqualTo(userId));
            Assert.That(member.CreatedAtUtc, Is.Not.EqualTo(default(DateTimeOffset)));
            Assert.That(member.UpdatedAtUtc, Is.Not.EqualTo(default(DateTimeOffset)));
        });
    }

    [Test]
    public async Task AssignMember_ToMissingSession_Throws()
    {
        var email = $"member-{Guid.NewGuid():N}@example.com";

        await using var context = CreateContext(new FakeCurrentUserContext(TenantA, Guid.NewGuid(), authenticated: true));
        var repository = new SessionMemberRepository(context, new FakeCurrentUserContext(TenantA, Guid.NewGuid(), authenticated: true));

        Assert.That(
            async () => await repository.AssignMemberAsync(Guid.NewGuid(), email, null, CancellationToken.None),
            Throws.TypeOf<SessionNotFoundException>());
    }

    [Test]
    public async Task Members_AreScopedPerTenant()
    {
        var email = $"member-{Guid.NewGuid():N}@example.com";
        var sessionId = await CreateSessionAsync(TenantA);

        await using (var addContext = CreateContext(new FakeCurrentUserContext(TenantA, Guid.NewGuid(), authenticated: true)))
        {
            var repository = new SessionMemberRepository(addContext, new FakeCurrentUserContext(TenantA, Guid.NewGuid(), authenticated: true));
            await repository.AssignMemberAsync(sessionId, email, "A member", CancellationToken.None);
        }

        await using var readA = CreateContext(new FakeCurrentUserContext(TenantA, Guid.Empty, authenticated: true));
        await using var readB = CreateContext(new FakeCurrentUserContext(TenantB, Guid.Empty, authenticated: true));

        var membersA = await new SessionMemberRepository(readA, new FakeCurrentUserContext(TenantA, Guid.Empty, authenticated: true))
            .GetMembersAsync(sessionId, CancellationToken.None);
        var membersB = await new SessionMemberRepository(readB, new FakeCurrentUserContext(TenantB, Guid.Empty, authenticated: true))
            .GetMembersAsync(sessionId, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(membersA.Select(m => m.Email), Does.Contain(email));
            Assert.That(membersB, Is.Empty);
        });
    }

    [Test]
    public async Task AssignMember_DuplicateEmail_Throws()
    {
        var email = $"dupe-{Guid.NewGuid():N}@example.com";
        var sessionId = await CreateSessionAsync(TenantA);

        await using var context = CreateContext(new FakeCurrentUserContext(TenantA, Guid.NewGuid(), authenticated: true));
        var repository = new SessionMemberRepository(context, new FakeCurrentUserContext(TenantA, Guid.NewGuid(), authenticated: true));

        await repository.AssignMemberAsync(sessionId, email, null, CancellationToken.None);

        Assert.That(
            async () => await repository.AssignMemberAsync(sessionId, email, null, CancellationToken.None),
            Throws.TypeOf<DuplicateSessionMemberException>());
    }

    [Test]
    public async Task RemoveMember_RemovesTheMember()
    {
        var email = $"remove-{Guid.NewGuid():N}@example.com";
        var sessionId = await CreateSessionAsync(TenantA);

        await using var context = CreateContext(new FakeCurrentUserContext(TenantA, Guid.NewGuid(), authenticated: true));
        var repository = new SessionMemberRepository(context, new FakeCurrentUserContext(TenantA, Guid.NewGuid(), authenticated: true));

        var member = await repository.AssignMemberAsync(sessionId, email, null, CancellationToken.None);

        var removed = await repository.RemoveMemberAsync(sessionId, member.Id, CancellationToken.None);
        var remaining = await repository.GetMembersAsync(sessionId, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(removed, Is.True);
            Assert.That(remaining.Select(m => m.Email), Does.Not.Contain(email));
        });
    }

    [Test]
    public async Task DeleteSession_CascadesItsMembers()
    {
        var userId = Guid.NewGuid();
        var email = $"cascade-{Guid.NewGuid():N}@example.com";
        var sessionId = await CreateSessionAsync(TenantA, userId);

        await using (var addContext = CreateContext(new FakeCurrentUserContext(TenantA, userId, authenticated: true)))
        {
            var repository = new SessionMemberRepository(addContext, new FakeCurrentUserContext(TenantA, userId, authenticated: true));
            await repository.AssignMemberAsync(sessionId, email, null, CancellationToken.None);
        }

        await using (var delete = CreateContext(new FakeCurrentUserContext(TenantA, userId, authenticated: true)))
        {
            var session = await delete.Sessions.SingleAsync(s => s.Id == sessionId);
            delete.Sessions.Remove(session);
            await delete.SaveChangesAsync();
        }

        await using var read = CreateContext(new FakeCurrentUserContext(TenantA, userId, authenticated: true));
        var remainingMembers = await read.SessionMembers
            .AsNoTracking()
            .Where(m => m.SessionId == sessionId)
            .ToListAsync();

        Assert.That(remainingMembers, Is.Empty);
    }

    [Test]
    public void AssignMember_WithNoTenantContext_IsRejectedFailClosed()
    {
        using var context = CreateContext(new FakeCurrentUserContext(Guid.Empty, Guid.Empty, authenticated: false));
        context.SessionMembers.Add(new SessionMember
        {
            SessionId = Guid.NewGuid(),
            Email = $"no-tenant-{Guid.NewGuid():N}@example.com"
        });

        Assert.That(() => context.SaveChanges(), Throws.TypeOf<InvalidOperationException>());
    }

    private static async Task<Guid> CreateSessionAsync(Guid tenantId, Guid? createdBy = null)
    {
        var userId = createdBy ?? Guid.NewGuid();
        await using var context = CreateContext(new FakeCurrentUserContext(tenantId, userId, authenticated: true));
        var session = new PlanningSession
        {
            Name = $"session-{Guid.NewGuid():N}",
            ProjectId = PersistenceTestData.AddProject(context, userId),
            CreatedByUserId = userId
        };
        context.Sessions.Add(session);
        await context.SaveChangesAsync();
        return session.Id;
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
