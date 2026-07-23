using Grpc.Core;
using PlanDeck.Application.Abstractions;
using PlanDeck.Application.Domain;
using PlanDeck.Application.Planning;
using PlanDeck.Application.Services;
using PlanDeck.Core.Shared.Contracts;
using PlanDeck.Core.Shared.Realtime;

namespace PlanDeck.Unit.Tests.Projects;

[TestFixture]
public sealed class ProjectGrpcServiceTests
{
    private static readonly Guid ProjectId = Guid.NewGuid();
    private RecordingProjectAccessResolver _access = null!;
    private FakeProjectRepository _repository = null!;
    private FakeConnectionRepository _connections = null!;
    private FakeSecretStore _secrets = null!;
    private ProjectGrpcService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _access = new RecordingProjectAccessResolver();
        _repository = new FakeProjectRepository();
        _connections = new FakeConnectionRepository();
        _secrets = new FakeSecretStore();
        _service = new ProjectGrpcService(
            _repository,
            _access,
            new FakeCurrentUserContext(),
            new FakeSessionRepository(),
            new FakePlanningRoomService(),
            _connections,
            _secrets,
            new FakeConnectionValidator(),
            TimeProvider.System);
    }

    [Test]
    public async Task GetProject_RequiresMemberRole()
    {
        await _service.GetProjectAsync(new GetProjectRequest { ProjectId = ProjectId });

        Assert.That(_access.Requirements, Is.EqualTo(new[] { ProjectRole.Member }));
    }

    [Test]
    public async Task AdminOperations_RequireAdminRole()
    {
        await _service.InviteMemberAsync(new InviteProjectMemberRequest
        {
            ProjectId = ProjectId,
            Email = "member@example.com",
            Role = ProjectRoleDto.Member
        });
        await _service.ChangeMemberRoleAsync(new ChangeProjectMemberRoleRequest
        {
            ProjectId = ProjectId,
            MemberId = Guid.NewGuid(),
            Role = ProjectRoleDto.Admin
        });
        await _service.RemoveMemberAsync(new RemoveProjectMemberRequest
        {
            ProjectId = ProjectId,
            MemberId = Guid.NewGuid()
        });
        await _service.AssignTeamAsync(new AssignProjectTeamRequest
        {
            ProjectId = ProjectId,
            TeamId = Guid.NewGuid()
        });
        await _service.UnassignTeamAsync(new UnassignProjectTeamRequest
        {
            ProjectId = ProjectId,
            TeamId = Guid.NewGuid()
        });

        Assert.That(
            _access.Requirements,
            Is.EqualTo(Enumerable.Repeat(ProjectRole.Admin, 5)));
    }

    [Test]
    public async Task OwnerOperations_RequireOwnerRole()
    {
        await _service.TransferOwnershipAsync(new TransferProjectOwnershipRequest
        {
            ProjectId = ProjectId,
            NewOwnerMemberId = Guid.NewGuid()
        });
        await _service.DeleteProjectAsync(new DeleteProjectRequest { ProjectId = ProjectId });

        Assert.That(
            _access.Requirements,
            Is.EqualTo(new[] { ProjectRole.Owner, ProjectRole.Owner }));
    }

    [Test]
    public void DeleteProject_WhenSqlDeleteFails_RecoversSecret()
    {
        _connections.Connection = CreateConnection();
        _repository.DeleteException = new ProjectPersistenceException(
            new InvalidOperationException("SQL failure"));

        Assert.ThrowsAsync<ProjectPersistenceException>(() =>
            _service.DeleteProjectAsync(new DeleteProjectRequest { ProjectId = ProjectId }));

        Assert.Multiple(() =>
        {
            Assert.That(_secrets.DeletedNames, Is.EqualTo(new[] { "project-secret" }));
            Assert.That(_secrets.RecoveredNames, Is.EqualTo(new[] { "project-secret" }));
        });
    }

    [Test]
    public void DeleteProject_WhenSecretRecoveryFails_ReturnsUnavailable()
    {
        _connections.Connection = CreateConnection();
        _repository.DeleteException = new ProjectPersistenceException(
            new InvalidOperationException("SQL failure"));
        _secrets.RecoverException = new ProjectSecretUnavailableException();

        var exception = Assert.ThrowsAsync<RpcException>(() =>
            _service.DeleteProjectAsync(new DeleteProjectRequest { ProjectId = ProjectId }));

        Assert.That(exception!.StatusCode, Is.EqualTo(StatusCode.Unavailable));
        Assert.That(_secrets.RecoveredNames, Is.EqualTo(new[] { "project-secret" }));
    }

    [Test]
    public void InviteMember_RejectsOwnerRole()
    {
        var exception = Assert.ThrowsAsync<RpcException>(() =>
            _service.InviteMemberAsync(new InviteProjectMemberRequest
            {
                ProjectId = ProjectId,
                Email = "owner@example.com",
                Role = ProjectRoleDto.Owner
            }));

        Assert.That(exception!.StatusCode, Is.EqualTo(StatusCode.InvalidArgument));
        Assert.That(_repository.InviteCalls, Is.Zero);
    }

    [TestCase(true, StatusCode.NotFound)]
    [TestCase(false, StatusCode.PermissionDenied)]
    public void InaccessibleProject_MapsToNonDisclosingStatus(
        bool hidden,
        StatusCode expectedStatus)
    {
        _access.Exception = hidden
            ? new ProjectNotFoundException(ProjectId)
            : new ProjectPermissionDeniedException(ProjectId, ProjectRole.Admin);

        var exception = Assert.ThrowsAsync<RpcException>(() =>
            _service.RemoveMemberAsync(new RemoveProjectMemberRequest
            {
                ProjectId = ProjectId,
                MemberId = Guid.NewGuid()
            }));

        Assert.That(exception!.StatusCode, Is.EqualTo(expectedStatus));
    }

    private sealed class RecordingProjectAccessResolver : IProjectAccessResolver
    {
        public List<ProjectRole> Requirements { get; } = [];

        public Exception? Exception { get; set; }

        public Task<ProjectRole?> GetEffectiveRoleAsync(
            Guid projectId,
            CancellationToken cancellationToken) =>
            Task.FromResult<ProjectRole?>(ProjectRole.Owner);

        public Task<ProjectRole> RequireRoleAsync(
            Guid projectId,
            ProjectRole minimumRole,
            CancellationToken cancellationToken)
        {
            Requirements.Add(minimumRole);
            if (Exception is not null)
            {
                throw Exception;
            }

            return Task.FromResult(ProjectRole.Owner);
        }
    }

    private sealed class FakeProjectRepository : IProjectRepository
    {
        public int InviteCalls { get; private set; }

        public Exception? DeleteException { get; set; }

        public Task<PlanDeckProject> CreateAsync(
            string name,
            string? description,
            string ownerEmail,
            CancellationToken cancellationToken) =>
            Task.FromResult(Project(name));

        public Task<IReadOnlyList<PlanDeckProject>> ListAccessibleAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PlanDeckProject>>([Project()]);

        public Task<PlanDeckProject?> GetAsync(
            Guid projectId,
            CancellationToken cancellationToken) =>
            Task.FromResult<PlanDeckProject?>(Project());

        public Task<IReadOnlyList<ProjectMember>> ListMembersAsync(
            Guid projectId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ProjectMember>>([]);

        public Task<IReadOnlyList<ProjectTeam>> ListTeamsAsync(
            Guid projectId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ProjectTeam>>([]);

        public Task<ProjectMember> InviteMemberAsync(
            Guid projectId,
            string email,
            ProjectRole role,
            CancellationToken cancellationToken)
        {
            InviteCalls++;
            return Task.FromResult(Member(email, role));
        }

        public Task RemoveMemberAsync(
            Guid projectId,
            Guid memberId,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<ProjectMember> ChangeMemberRoleAsync(
            Guid projectId,
            Guid memberId,
            ProjectRole role,
            CancellationToken cancellationToken) =>
            Task.FromResult(Member("member@example.com", role));

        public Task<ProjectTeam> AssignTeamAsync(
            Guid projectId,
            Guid teamId,
            CancellationToken cancellationToken) =>
            Task.FromResult(new ProjectTeam { ProjectId = projectId, TeamId = teamId });

        public Task UnassignTeamAsync(
            Guid projectId,
            Guid teamId,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task TransferOwnershipAsync(
            Guid projectId,
            Guid newOwnerMemberId,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task DeleteAsync(
            Guid projectId,
            CancellationToken cancellationToken)
        {
            if (DeleteException is not null)
            {
                throw DeleteException;
            }

            return Task.CompletedTask;
        }

        private static PlanDeckProject Project(string name = "Project") => new()
        {
            Id = ProjectId,
            Name = name,
            CreatedByUserId = Guid.NewGuid()
        };

        private static ProjectMember Member(string email, ProjectRole role) => new()
        {
            ProjectId = ProjectId,
            Email = email,
            Role = role
        };
    }

    private sealed class FakeSessionRepository : ISessionRepository
    {
        public Task<PlanningSession> CreateSessionAsync(
            PlanningSession session,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<PlanningSession>> GetSessionsAsync(
            Guid projectId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PlanningSession>>([]);

        public Task<IReadOnlyList<Guid>> GetSessionIdsAsync(
            Guid projectId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Guid>>([]);

        public Task<PlanningSession?> GetSessionAsync(
            Guid id,
            CancellationToken cancellationToken) =>
            Task.FromResult<PlanningSession?>(null);

        public Task<PlanningSession> UpdateSessionAsync(
            PlanningSession session,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> DeleteSessionAsync(Guid id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> SetAgreedEstimateAsync(
            Guid sessionId,
            Guid taskId,
            string? estimate,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> SetAdoRevisionAsync(
            Guid sessionId,
            Guid taskId,
            int revision,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<GuestSessionReference?> GetActiveSessionByShareCodeAsync(
            string shareCode,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> ShareCodeExistsAsync(string shareCode, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FakePlanningRoomService : IPlanningRoomService
    {
        public PlanningRoomState EnsureSeeded(
            RoomKey key,
            IReadOnlyList<PlanningRoomTaskSnapshot> tasks,
            IReadOnlyList<string> scaleValues) =>
            throw new NotSupportedException();

        public PlanningRoomState SyncTasks(RoomKey key, IReadOnlyList<PlanningRoomTaskSnapshot> tasks) =>
            throw new NotSupportedException();

        public PlanningRoomState Join(
            RoomKey key,
            string participantId,
            string displayName,
            string connectionId) =>
            throw new NotSupportedException();

        public PlanningRoomState Leave(RoomKey key, string participantId, string connectionId) =>
            throw new NotSupportedException();

        public (RoomKey Key, PlanningRoomState State)? Disconnect(string connectionId) =>
            throw new NotSupportedException();

        public PlanningRoomState CastVote(RoomKey key, string participantId, string vote) =>
            throw new NotSupportedException();

        public PlanningRoomState RevealVotes(RoomKey key) =>
            throw new NotSupportedException();

        public PlanningRoomState ResetRound(RoomKey key, Guid taskId) =>
            throw new NotSupportedException();

        public PlanningRoomState SetActiveTask(RoomKey key, Guid taskId) =>
            throw new NotSupportedException();

        public PlanningRoomState ApplyAgreedEstimate(RoomKey key, Guid taskId, string? estimate) =>
            throw new NotSupportedException();

        public bool IsValidEstimate(RoomKey key, string? estimate) =>
            throw new NotSupportedException();

        public PlanningRoomState GetState(RoomKey key) =>
            throw new NotSupportedException();

        public bool InvalidateSession(RoomKey key) => true;

        public int RemoveInactiveRooms(DateTimeOffset inactiveSince) =>
            throw new NotSupportedException();
    }

    private sealed class FakeConnectionRepository
        : IProjectAzureDevOpsConnectionRepository
    {
        public ProjectAzureDevOpsConnection? Connection { get; set; }

        public Task<ProjectAzureDevOpsConnection?> GetAsync(
            Guid projectId,
            CancellationToken cancellationToken) =>
            Task.FromResult(Connection);

        public Task AddAsync(
            ProjectAzureDevOpsConnection connection,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task UpdateAsync(
            ProjectAzureDevOpsConnection connection,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task DeleteAsync(
            ProjectAzureDevOpsConnection connection,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task LockTargetAsync(
            Guid projectId,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class FakeSecretStore : IProjectSecretStore
    {
        public List<string> DeletedNames { get; } = [];

        public List<string> RecoveredNames { get; } = [];

        public Exception? RecoverException { get; set; }

        public Task<string> CreateAsync(string value, CancellationToken cancellationToken) =>
            Task.FromResult($"pat-{Guid.NewGuid():N}");

        public Task<string> GetLatestAsync(
            string secretName,
            CancellationToken cancellationToken) =>
            Task.FromResult("pat");

        public Task RotateAsync(
            string secretName,
            string value,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task SoftDeleteAsync(
            string secretName,
            CancellationToken cancellationToken)
        {
            DeletedNames.Add(secretName);
            return Task.CompletedTask;
        }

        public Task RecoverAsync(
            string secretName,
            CancellationToken cancellationToken)
        {
            RecoveredNames.Add(secretName);
            if (RecoverException is not null)
            {
                throw RecoverException;
            }

            return Task.CompletedTask;
        }

        public void Invalidate(string secretName)
        {
        }
    }

    private static ProjectAzureDevOpsConnection CreateConnection() => new()
    {
        ProjectId = ProjectId,
        OrganizationUrl = "https://dev.azure.com/test",
        AzureDevOpsProject = "project",
        EstimateField = "Microsoft.VSTS.Scheduling.StoryPoints",
        DescriptionField = "System.Description",
        ReproStepsField = "Microsoft.VSTS.TCM.ReproSteps",
        AcceptanceCriteriaField = "Microsoft.VSTS.Common.AcceptanceCriteria",
        SecretName = "project-secret"
    };

    private sealed class FakeConnectionValidator : IAzureDevOpsConnectionValidator
    {
        public Task ValidateAsync(
            AzureDevOpsConnectionValidationRequest request,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class FakeCurrentUserContext : ICurrentUserContext
    {
        public Guid TenantId { get; } = Guid.NewGuid();

        public Guid UserId { get; } = Guid.NewGuid();

        public bool IsAuthenticated => true;

        public string? DisplayName => "Owner";

        public string? Email => "owner@example.com";
    }
}
