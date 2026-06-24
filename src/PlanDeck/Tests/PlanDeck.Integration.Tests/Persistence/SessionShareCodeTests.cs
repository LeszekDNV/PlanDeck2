using Microsoft.EntityFrameworkCore;
using PlanDeck.Application.Abstractions;
using PlanDeck.Application.Domain;
using PlanDeck.Infrastructure.Persistence;

namespace PlanDeck.Integration.Tests.Persistence;

[TestFixture]
public sealed class SessionShareCodeTests
{
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Test]
    public async Task GetActiveSessionByShareCode_ReturnsActiveSession_IgnoringTenant()
    {
        var userId = Guid.NewGuid();
        var code = $"CODE{Guid.NewGuid():N}"[..12];
        var sessionId = await SeedSessionAsync(TenantA, userId, SessionStatus.Active, code);

        // Resolve from an empty/foreign tenant context — the redeem path never knows the tenant.
        await using var context = CreateContext(new FakeCurrentUserContext(Guid.Empty, Guid.Empty, authenticated: false));
        var repository = new SessionRepository(context, new FakeCurrentUserContext(Guid.Empty, Guid.Empty, authenticated: false));

        var result = await repository.GetActiveSessionByShareCodeAsync(code, CancellationToken.None);

        Assert.That(result, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(result!.SessionId, Is.EqualTo(sessionId));
            Assert.That(result.TenantId, Is.EqualTo(TenantA));
        });
    }

    [Test]
    public async Task GetActiveSessionByShareCode_ReturnsNull_ForDraftSession()
    {
        var userId = Guid.NewGuid();
        var code = $"CODE{Guid.NewGuid():N}"[..12];
        await SeedSessionAsync(TenantA, userId, SessionStatus.Draft, code);

        await using var context = CreateContext(new FakeCurrentUserContext(Guid.Empty, Guid.Empty, authenticated: false));
        var repository = new SessionRepository(context, new FakeCurrentUserContext(Guid.Empty, Guid.Empty, authenticated: false));

        var result = await repository.GetActiveSessionByShareCodeAsync(code, CancellationToken.None);

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task GetActiveSessionByShareCode_ReturnsNull_ForUnknownCode()
    {
        await using var context = CreateContext(new FakeCurrentUserContext(Guid.Empty, Guid.Empty, authenticated: false));
        var repository = new SessionRepository(context, new FakeCurrentUserContext(Guid.Empty, Guid.Empty, authenticated: false));

        var result = await repository.GetActiveSessionByShareCodeAsync($"MISSING{Guid.NewGuid():N}"[..12], CancellationToken.None);

        Assert.That(result, Is.Null);
    }

    private static async Task<Guid> SeedSessionAsync(Guid tenantId, Guid userId, SessionStatus status, string shareCode)
    {
        await using var context = CreateContext(new FakeCurrentUserContext(tenantId, userId, authenticated: true));
        var session = new PlanningSession
        {
            Name = $"session-{Guid.NewGuid():N}",
            CreatedByUserId = userId,
            Status = status,
            ShareCode = shareCode
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
