using Microsoft.EntityFrameworkCore;
using PlanDeck.Application.Abstractions;
using PlanDeck.Application.Domain;
using PlanDeck.Infrastructure.Persistence;

namespace PlanDeck.Integration.Tests.Persistence;

[TestFixture]
public sealed class SessionRepositoryEstimateTests
{
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Test]
    public async Task SetAgreedEstimate_PersistsAndReReadsTheValue()
    {
        var userId = Guid.NewGuid();
        var (sessionId, taskId) = await SeedSessionWithTaskAsync(TenantA, userId);

        bool updated;
        await using (var context = CreateContext(new FakeCurrentUserContext(TenantA, userId, authenticated: true)))
        {
            var repository = new SessionRepository(context, new FakeCurrentUserContext(TenantA, userId, authenticated: true));
            updated = await repository.SetAgreedEstimateAsync(sessionId, taskId, "5", CancellationToken.None);
        }

        await using var read = CreateContext(new FakeCurrentUserContext(TenantA, userId, authenticated: true));
        var loaded = await read.SessionTasks
            .AsNoTracking()
            .SingleAsync(t => t.Id == taskId);

        Assert.Multiple(() =>
        {
            Assert.That(updated, Is.True);
            Assert.That(loaded.AgreedEstimate, Is.EqualTo("5"));
        });
    }

    [Test]
    public async Task SetAgreedEstimate_ForTaskOutsideTenant_ReturnsFalse()
    {
        var userId = Guid.NewGuid();
        var (sessionId, taskId) = await SeedSessionWithTaskAsync(TenantA, userId);

        bool updated;
        await using (var context = CreateContext(new FakeCurrentUserContext(TenantB, Guid.NewGuid(), authenticated: true)))
        {
            var repository = new SessionRepository(context, new FakeCurrentUserContext(TenantB, Guid.NewGuid(), authenticated: true));
            updated = await repository.SetAgreedEstimateAsync(sessionId, taskId, "8", CancellationToken.None);
        }

        await using var read = CreateContext(new FakeCurrentUserContext(TenantA, userId, authenticated: true));
        var loaded = await read.SessionTasks
            .AsNoTracking()
            .SingleAsync(t => t.Id == taskId);

        Assert.Multiple(() =>
        {
            Assert.That(updated, Is.False);
            Assert.That(loaded.AgreedEstimate, Is.Null);
        });
    }

    private static async Task<(Guid SessionId, Guid TaskId)> SeedSessionWithTaskAsync(Guid tenantId, Guid userId)
    {
        await using var context = CreateContext(new FakeCurrentUserContext(tenantId, userId, authenticated: true));
        var session = new PlanningSession
        {
            Name = $"session-{Guid.NewGuid():N}",
            CreatedByUserId = userId,
            ScaleValues = ["1", "2", "3", "5", "8"],
            Tasks = { new SessionTask { Title = "Task", Source = TaskSource.AdHoc, SortOrder = 0 } }
        };
        context.Sessions.Add(session);
        await context.SaveChangesAsync();
        return (session.Id, session.Tasks.Single().Id);
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
