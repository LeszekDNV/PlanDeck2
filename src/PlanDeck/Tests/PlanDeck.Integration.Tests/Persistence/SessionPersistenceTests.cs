using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using PlanDeck.Application.Abstractions;
using PlanDeck.Application.Domain;
using PlanDeck.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

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
                ProjectId = PersistenceTestData.AddProject(write, userId),
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
                ProjectId = PersistenceTestData.AddProject(write, userId),
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
    public async Task Repository_GetSessionsAsync_IsScopedToRequestedProject()
    {
        var userId = Guid.NewGuid();
        var projectA = Guid.NewGuid();
        var projectB = Guid.NewGuid();
        var nameA = $"a-{Guid.NewGuid():N}";
        var nameB = $"b-{Guid.NewGuid():N}";

        await using (var write = CreateContext(new FakeCurrentUserContext(TenantA, userId, authenticated: true)))
        {
            write.Projects.AddRange(
                new PlanDeckProject { Id = projectA, Name = $"project-{projectA:N}", CreatedByUserId = userId },
                new PlanDeckProject { Id = projectB, Name = $"project-{projectB:N}", CreatedByUserId = userId });

            write.Sessions.AddRange(
                new PlanningSession { Name = nameA, ProjectId = projectA, CreatedByUserId = userId },
                new PlanningSession { Name = nameB, ProjectId = projectB, CreatedByUserId = userId });

            await write.SaveChangesAsync();
        }

        await using var read = CreateContext(new FakeCurrentUserContext(TenantA, userId, authenticated: true));
        var repository = new SessionRepository(read, new FakeCurrentUserContext(TenantA, userId, authenticated: true));

        var sessions = await repository.GetSessionsAsync(projectA, CancellationToken.None);

        Assert.That(sessions.Select(s => s.Name), Is.EqualTo(new[] { nameA }));
    }

    [Test]
    public async Task SessionProjectForeignKey_UsesCascadeDeleteBehavior()
    {
        await using var connection = new SqlConnection(AspireAppFixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT TOP(1) [delete_referential_action_desc]
            FROM sys.foreign_keys
            WHERE [name] = 'FK_Sessions_Projects_TenantId_ProjectId';
            """;

        var action = (string?)await command.ExecuteScalarAsync();

        Assert.That(action, Is.EqualTo("CASCADE"));
    }

    [Test]
    public async Task SessionProjectCascadeMigration_UpAndDownPathsSucceed()
    {
        var databaseName = $"PlanDeckMigration_{Guid.NewGuid():N}";
        var masterConnectionString = new SqlConnectionStringBuilder(AspireAppFixture.ConnectionString)
        {
            InitialCatalog = "master"
        }.ConnectionString;
        var migrationConnectionString = new SqlConnectionStringBuilder(AspireAppFixture.ConnectionString)
        {
            InitialCatalog = databaseName
        }.ConnectionString;
        try
        {
            await using var context = CreateContext(
                new FakeCurrentUserContext(TenantA, Guid.NewGuid(), authenticated: true),
                migrationConnectionString);
            var migrator = context.Database.GetService<IMigrator>();
            var migrations = context.Database.GetMigrations().ToArray();
            Assert.That(migrations.Length, Is.GreaterThan(1));
            var previous = migrations[^2];
            var latest = migrations[^1];

            await migrator.MigrateAsync(previous);
            await migrator.MigrateAsync(latest);
            await migrator.MigrateAsync(previous);
            await migrator.MigrateAsync(latest);
        }
        finally
        {
            await using var master = new SqlConnection(masterConnectionString);
            await master.OpenAsync();
            await using var cleanup = master.CreateCommand();
            cleanup.CommandText = $"""
                IF DB_ID(N'{databaseName}') IS NOT NULL
                BEGIN
                    ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    DROP DATABASE [{databaseName}];
                END
                """;
            await cleanup.ExecuteNonQueryAsync();
        }
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
                ProjectId = PersistenceTestData.AddProject(write, userId),
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
        var projectId = Guid.NewGuid();
        var sessionName = $"custom-scale-{Guid.NewGuid():N}";
        Guid sessionId;

        await using (var write = CreateContext(new FakeCurrentUserContext(TenantA, userId, authenticated: true)))
        {
            var session = new PlanningSession
            {
                Name = sessionName,
                ProjectId = projectId,
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
            write.Projects.Add(new PlanDeckProject
            {
                Id = projectId,
                Name = $"project-{projectId:N}",
                CreatedByUserId = userId
            });
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
            Assert.That(loaded.Name, Is.EqualTo(sessionName));
            Assert.That(loaded.ProjectId, Is.EqualTo(projectId));
            Assert.That(loaded.ScaleType, Is.EqualTo(VotingScaleType.Custom));
            Assert.That(loaded.ScaleValues, Is.EqualTo(new[] { "XS", "S", "M", "L" }));
            Assert.That(loaded.Tasks, Has.Count.EqualTo(3));
            Assert.That(orderedTitles, Is.EqualTo(new[] { "Task 1", "Task 2", "Task 3" }));
            Assert.That(loaded.Tasks.Single(task => task.Title == "Task 1").AdoWorkItemId, Is.EqualTo(77));
        });
    }

    [Test]
    public async Task CreateSession_WhenTaskConstraintFails_NoPartialAggregatePersists()
    {
        var userId = Guid.NewGuid();
        var sessionName = $"atomic-failure-{Guid.NewGuid():N}";
        var firstTaskTitle = $"duplicate-a-{Guid.NewGuid():N}";
        var secondTaskTitle = $"duplicate-b-{Guid.NewGuid():N}";

        await using (var write = CreateContext(new FakeCurrentUserContext(TenantA, userId, authenticated: true)))
        {
            var session = new PlanningSession
            {
                Name = sessionName,
                ProjectId = PersistenceTestData.AddProject(write, userId),
                CreatedByUserId = userId,
                Tasks =
                {
                    new SessionTask
                    {
                        Title = firstTaskTitle,
                        Source = TaskSource.AzureDevOps,
                        AdoWorkItemId = 88
                    },
                    new SessionTask
                    {
                        Title = secondTaskTitle,
                        Source = TaskSource.AzureDevOps,
                        AdoWorkItemId = 88
                    }
                }
            };

            write.Sessions.Add(session);
            Assert.That(
                async () => await write.SaveChangesAsync(),
                Throws.TypeOf<DbUpdateException>());
        }
        await using var read = CreateContext(new FakeCurrentUserContext(TenantA, userId, authenticated: true));
        var sessionExists = await read.Sessions.AnyAsync(session => session.Name == sessionName);
        var taskExists = await read.SessionTasks.AnyAsync(task =>
            task.Title == firstTaskTitle || task.Title == secondTaskTitle);

        Assert.Multiple(() =>
        {
            Assert.That(sessionExists, Is.False);
            Assert.That(taskExists, Is.False);
        });
    }

    private static async Task<Guid> CreateSessionAsync(Guid tenantId, string name)
    {
        await using var context = CreateContext(new FakeCurrentUserContext(tenantId, Guid.NewGuid(), authenticated: true));
        var userId = Guid.NewGuid();
        var session = new PlanningSession
        {
            Name = name,
            ProjectId = PersistenceTestData.AddProject(context, userId),
            CreatedByUserId = userId
        };
        context.Sessions.Add(session);
        await context.SaveChangesAsync();
        return session.Id;
    }

    private static PlanDeckDbContext CreateContext(
        ICurrentUserContext currentUser,
        string? connectionString = null)
    {
        var options = new DbContextOptionsBuilder<PlanDeckDbContext>()
            .UseSqlServer(connectionString ?? AspireAppFixture.ConnectionString, sql => sql.EnableRetryOnFailure())
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
