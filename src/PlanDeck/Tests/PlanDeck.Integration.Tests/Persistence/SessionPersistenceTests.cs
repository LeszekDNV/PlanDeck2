using Microsoft.EntityFrameworkCore;
using PlanDeck.Application.Abstractions;
using PlanDeck.Application.Domain;
using PlanDeck.Infrastructure.Persistence;

namespace PlanDeck.Integration.Tests.Persistence;

[TestFixture]
public sealed class SessionPersistenceTests
{
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Test]
    public async Task CreateSession_WithTasks_RoundTripsAndStampsTenantAudit()
    {
        var userId = Guid.NewGuid();
        var name = $"session-{Guid.NewGuid():N}";

        Guid sessionId;
        await using (var write = CreateContext(new FakeCurrentUserContext(TenantA, userId, authenticated: true)))
        {
            var session = new PlanningSession
            {
                Name = name,
                CreatedByUserId = userId,
                ScaleType = VotingScaleType.Fibonacci,
                ScaleValues = ["1", "2", "3", "5", "8"],
                Tasks =
                {
                    new SessionTask { Title = "Ad-hoc task", Source = TaskSource.AdHoc, SortOrder = 0 },
                    new SessionTask
                    {
                        Title = "Imported task",
                        Source = TaskSource.AzureDevOps,
                        SortOrder = 1,
                        AdoWorkItemId = 42,
                        AdoRevision = 3,
                        WorkItemType = "Bug",
                        State = "Active"
                    }
                }
            };

            write.Sessions.Add(session);
            await write.SaveChangesAsync();
            sessionId = session.Id;
        }

        await using var read = CreateContext(new FakeCurrentUserContext(TenantA, userId, authenticated: true));
        var loaded = await read.Sessions
            .AsNoTracking()
            .Include(s => s.Tasks)
            .SingleAsync(s => s.Id == sessionId);

        Assert.Multiple(() =>
        {
            Assert.That(loaded.TenantId, Is.EqualTo(TenantA));
            Assert.That(loaded.CreatedByUserId, Is.EqualTo(userId));
            Assert.That(loaded.CreatedAtUtc, Is.Not.EqualTo(default(DateTimeOffset)));
            Assert.That(loaded.Status, Is.EqualTo(SessionStatus.Draft));
            Assert.That(loaded.ScaleValues, Is.EqualTo(new[] { "1", "2", "3", "5", "8" }));
            Assert.That(loaded.Tasks, Has.Count.EqualTo(2));
            Assert.That(loaded.Tasks.Select(t => t.AdoWorkItemId), Does.Contain(42));
        });
    }

    [Test]
    public async Task DeleteSession_CascadesItsTasks()
    {
        var userId = Guid.NewGuid();
        Guid sessionId;

        await using (var write = CreateContext(new FakeCurrentUserContext(TenantA, userId, authenticated: true)))
        {
            var session = new PlanningSession
            {
                Name = $"session-{Guid.NewGuid():N}",
                CreatedByUserId = userId,
                Tasks = { new SessionTask { Title = "Task", Source = TaskSource.AdHoc } }
            };
            write.Sessions.Add(session);
            await write.SaveChangesAsync();
            sessionId = session.Id;
        }

        await using (var delete = CreateContext(new FakeCurrentUserContext(TenantA, userId, authenticated: true)))
        {
            var session = await delete.Sessions.SingleAsync(s => s.Id == sessionId);
            delete.Sessions.Remove(session);
            await delete.SaveChangesAsync();
        }

        await using var read = CreateContext(new FakeCurrentUserContext(TenantA, userId, authenticated: true));
        var remainingTasks = await read.SessionTasks
            .AsNoTracking()
            .Where(t => t.SessionId == sessionId)
            .ToListAsync();

        Assert.That(remainingTasks, Is.Empty);
    }

    [Test]
    public async Task Sessions_AreScopedPerTenant_BothDirections()
    {
        var nameA = $"a-{Guid.NewGuid():N}";
        var nameB = $"b-{Guid.NewGuid():N}";

        await CreateSessionAsync(TenantA, nameA);
        await CreateSessionAsync(TenantB, nameB);

        await using var readA = CreateContext(new FakeCurrentUserContext(TenantA, Guid.Empty, authenticated: true));
        await using var readB = CreateContext(new FakeCurrentUserContext(TenantB, Guid.Empty, authenticated: true));

        var sessionsA = await readA.Sessions.AsNoTracking().Select(s => s.Name).ToListAsync();
        var sessionsB = await readB.Sessions.AsNoTracking().Select(s => s.Name).ToListAsync();

        Assert.Multiple(() =>
        {
            Assert.That(sessionsA, Does.Contain(nameA));
            Assert.That(sessionsA, Does.Not.Contain(nameB));
            Assert.That(sessionsB, Does.Contain(nameB));
            Assert.That(sessionsB, Does.Not.Contain(nameA));
        });
    }

    [Test]
    public void CreateSession_WithNoTenantContext_IsRejectedFailClosed()
    {
        using var context = CreateContext(new FakeCurrentUserContext(Guid.Empty, Guid.Empty, authenticated: false));
        context.Sessions.Add(new PlanningSession { Name = $"no-tenant-{Guid.NewGuid():N}" });

        Assert.That(() => context.SaveChanges(), Throws.TypeOf<InvalidOperationException>());
    }

    [Test]
    public async Task SessionTask_Description_RoundTripsAndIsTenantIsolated()
    {
        var userId = Guid.NewGuid();
        var description = $"## Notes\n\nMarkdown **body** {Guid.NewGuid():N}";
        Guid sessionId;

        await using (var write = CreateContext(new FakeCurrentUserContext(TenantA, userId, authenticated: true)))
        {
            var session = new PlanningSession
            {
                Name = $"session-{Guid.NewGuid():N}",
                CreatedByUserId = userId,
                Tasks =
                {
                    new SessionTask { Title = "Described task", Source = TaskSource.AdHoc, Description = description }
                }
            };
            write.Sessions.Add(session);
            await write.SaveChangesAsync();
            sessionId = session.Id;
        }

        await using (var read = CreateContext(new FakeCurrentUserContext(TenantA, userId, authenticated: true)))
        {
            var task = await read.SessionTasks
                .AsNoTracking()
                .SingleAsync(t => t.SessionId == sessionId);

            Assert.That(task.Description, Is.EqualTo(description));
        }

        await using var otherTenant = CreateContext(new FakeCurrentUserContext(TenantB, Guid.NewGuid(), authenticated: true));
        var leaked = await otherTenant.SessionTasks
            .AsNoTracking()
            .Where(t => t.SessionId == sessionId)
            .ToListAsync();

        Assert.That(leaked, Is.Empty);
    }

    [Test]
    public async Task CreateSession_WithCustomScaleAndOrderedTasks_RoundTripsExactly()
    {
        var userId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        Guid sessionId;

        await using (var write = CreateContext(new FakeCurrentUserContext(TenantA, userId, authenticated: true)))
        {
            var session = new PlanningSession
            {
                Name = $"custom-scale-{Guid.NewGuid():N}",
                TeamId = teamId,
                CreatedByUserId = userId,
                ScaleType = VotingScaleType.Custom,
                ScaleValues = ["XS", "S", "M", "L"],
                Tasks =
                {
                    new SessionTask { Title = "Task 2", Source = TaskSource.AdHoc, SortOrder = 1 },
                    new SessionTask { Title = "Task 1", Source = TaskSource.AzureDevOps, SortOrder = 0, AdoWorkItemId = 77, AdoRevision = 9 },
                    new SessionTask { Title = "Task 3", Source = TaskSource.AdHoc, SortOrder = 2 }
                }
            };

            write.Sessions.Add(session);
            await write.SaveChangesAsync();
            sessionId = session.Id;
        }

        await using var read = CreateContext(new FakeCurrentUserContext(TenantA, userId, authenticated: true));
        var loaded = await read.Sessions
            .AsNoTracking()
            .Include(s => s.Tasks)
            .SingleAsync(s => s.Id == sessionId);

        var orderedTitles = loaded.Tasks
            .OrderBy(task => task.SortOrder)
            .Select(task => task.Title)
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(loaded.TeamId, Is.EqualTo(teamId));
            Assert.That(loaded.ScaleType, Is.EqualTo(VotingScaleType.Custom));
            Assert.That(loaded.ScaleValues, Is.EqualTo(new[] { "XS", "S", "M", "L" }));
            Assert.That(loaded.Tasks, Has.Count.EqualTo(3));
            Assert.That(orderedTitles, Is.EqualTo(new[] { "Task 1", "Task 2", "Task 3" }));
            Assert.That(loaded.Tasks.Single(task => task.Title == "Task 1").AdoWorkItemId, Is.EqualTo(77));
        });
    }

    private static async Task<Guid> CreateSessionAsync(Guid tenantId, string name)
    {
        await using var context = CreateContext(new FakeCurrentUserContext(tenantId, Guid.NewGuid(), authenticated: true));
        var session = new PlanningSession { Name = name, CreatedByUserId = Guid.NewGuid() };
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
