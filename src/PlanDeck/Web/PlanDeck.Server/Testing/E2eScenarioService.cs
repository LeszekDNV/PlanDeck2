using Microsoft.EntityFrameworkCore;
using PlanDeck.Application.Abstractions;
using PlanDeck.Application.Domain;
using PlanDeck.Infrastructure.Persistence;

namespace PlanDeck.Server.Testing;

public sealed class E2eScenarioService(
    PlanDeckDbContext dbContext,
    ICurrentUserContext currentUserContext)
{
    public async Task<E2eScenarioSeedResponse> SeedAsync(
        E2eScenarioSeedRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.RunId == Guid.Empty)
        {
            throw new InvalidOperationException("runId must be a non-empty GUID.");
        }

        if (request.TaskCount is < 0 or > 50)
        {
            throw new InvalidOperationException("taskCount must be in the range 0..50.");
        }

        await CleanupAsync(request.RunId, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var projectId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var projectName = BuildProjectName(request.RunId);
        var sessionName = BuildSessionName(request.RunId);
        var createdByUserId = TestMemberIdentities.Owner.AppUserId;

        dbContext.Projects.Add(new PlanDeckProject
        {
            Id = projectId,
            Name = projectName,
            Description = $"Deterministic E2E scenario for run {request.RunId:N}.",
            CreatedByUserId = createdByUserId
        });

        dbContext.ProjectMembers.AddRange(
            BuildAcceptedProjectMember(
                projectId,
                TestMemberIdentities.Owner,
                ProjectRole.Owner,
                now),
            BuildAcceptedProjectMember(
                projectId,
                TestMemberIdentities.Admin,
                ProjectRole.Admin,
                now),
            BuildAcceptedProjectMember(
                projectId,
                TestMemberIdentities.Member,
                ProjectRole.Member,
                now));

        dbContext.Sessions.Add(new PlanningSession
        {
            Id = sessionId,
            Name = sessionName,
            ProjectId = projectId,
            CreatedByUserId = createdByUserId,
            Status = request.SessionStatus,
            ScaleType = VotingScaleType.Fibonacci,
            ScaleValues = ["1", "2", "3", "5", "8", "13", "?"],
            ShareCode = request.SessionStatus == SessionStatus.Active
                ? BuildShareCode(request.RunId)
                : null
        });

        for (var index = 0; index < request.TaskCount; index++)
        {
            dbContext.SessionTasks.Add(new SessionTask
            {
                Id = Guid.NewGuid(),
                SessionId = sessionId,
                Title = $"{request.RunId:N}-task-{index + 1}",
                Description = "Deterministic E2E task",
                Source = TaskSource.AdHoc,
                SortOrder = index
            });
        }

        dbContext.SessionMembers.Add(new SessionMember
        {
            Id = Guid.NewGuid(),
            SessionId = sessionId,
            Email = TestMemberIdentities.Member.Email,
            DisplayName = TestMemberIdentities.Member.DisplayName,
            AssignedByUserId = createdByUserId
        });

        await dbContext.SaveChangesAsync(cancellationToken);

        return new E2eScenarioSeedResponse(
            request.RunId,
            projectId,
            sessionId,
            TestMemberIdentities.Owner.AppUserId,
            TestMemberIdentities.Admin.AppUserId,
            TestMemberIdentities.Member.AppUserId);
    }

    public async Task<E2eScenarioCleanupResponse> CleanupAsync(
        Guid runId,
        CancellationToken cancellationToken = default)
    {
        if (runId == Guid.Empty)
        {
            throw new InvalidOperationException("runId must be a non-empty GUID.");
        }

        var projectName = BuildProjectName(runId);
        var projects = await dbContext.Projects
            .Where(project => project.Name == projectName)
            .ToListAsync(cancellationToken);
        if (projects.Count == 0)
        {
            return new E2eScenarioCleanupResponse(runId, 0);
        }

        dbContext.Projects.RemoveRange(projects);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new E2eScenarioCleanupResponse(runId, projects.Count);
    }

    private ProjectMember BuildAcceptedProjectMember(
        Guid projectId,
        TestMemberIdentity identity,
        ProjectRole role,
        DateTimeOffset acceptedAtUtc) =>
        new()
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            AppUserId = identity.AppUserId,
            Email = identity.Email,
            NormalizedEmail = identity.Email.ToUpperInvariant(),
            Role = role,
            Status = InvitationStatus.Accepted,
            InvitedByUserId = TestMemberIdentities.Owner.AppUserId,
            AcceptedAtUtc = acceptedAtUtc
        };

    private static string BuildProjectName(Guid runId) => $"e2e-scenario-project-{runId:N}";

    private static string BuildSessionName(Guid runId) => $"e2e-scenario-session-{runId:N}";

    private static string BuildShareCode(Guid runId) => runId.ToString("N")[..12].ToUpperInvariant();
}

public sealed record E2eScenarioSeedRequest(
    Guid RunId,
    SessionStatus SessionStatus = SessionStatus.Draft,
    int TaskCount = 1);

public sealed record E2eScenarioSeedResponse(
    Guid RunId,
    Guid ProjectId,
    Guid SessionId,
    Guid OwnerUserId,
    Guid AdminUserId,
    Guid MemberUserId);

public sealed record E2eScenarioCleanupResponse(
    Guid RunId,
    int DeletedProjectCount);
