using System.Text.Json;
using Grpc.Core;
using PlanDeck.Application.Abstractions;
using PlanDeck.Application.Domain;
using PlanDeck.Application.Planning;
using PlanDeck.Application.Services;
using PlanDeck.Core.Shared.Contracts;
using PlanDeck.Core.Shared.Realtime;

namespace PlanDeck.Unit.Tests.Projects;

[TestFixture]
public sealed class ProjectConnectionGrpcServiceTests
{
    private static readonly Guid ProjectId = Guid.NewGuid();
    private StubProjectRepository _projects = null!;
    private StubConnectionRepository _connections = null!;
    private InMemoryProjectSecretStore _secrets = null!;
    private StubConnectionValidator _validator = null!;
    private RoleAccessResolver _access = null!;
    private StubSessionRepository _sessions = null!;
    private RecordingPlanningRoomService _planningRooms = null!;
    private ProjectGrpcService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _projects = new StubProjectRepository();
        _connections = new StubConnectionRepository();
        _secrets = new InMemoryProjectSecretStore();
        _validator = new StubConnectionValidator();
        _access = new RoleAccessResolver(ProjectRole.Owner);
        _sessions = new StubSessionRepository();
        _planningRooms = new RecordingPlanningRoomService();
        _service = new ProjectGrpcService(
            _projects,
            _access,
            new StubCurrentUserContext(),
            _sessions,
            _planningRooms,
            _connections,
            _secrets,
            _validator,
            TimeProvider.System);
    }

    [TestCase(ProjectRole.Admin)]
    [TestCase(ProjectRole.Member)]
    public void ConnectionMutations_NonOwnerIsDeniedBeforeExternalCalls(ProjectRole role)
    {
        _access.Role = role;

        var exceptions = new[]
        {
            Assert.ThrowsAsync<RpcException>(() =>
                _service.ConfigureConnectionAsync(ConfigureRequest())),
            Assert.ThrowsAsync<RpcException>(() =>
                _service.UpdateConnectionAsync(UpdateRequest())),
            Assert.ThrowsAsync<RpcException>(() =>
                _service.RotateConnectionPatAsync(new RotateProjectConnectionPatRequest
                {
                    ProjectId = ProjectId,
                    PersonalAccessToken = "new-pat"
                })),
            Assert.ThrowsAsync<RpcException>(() =>
                _service.SetConnectionEnabledAsync(new SetProjectConnectionEnabledRequest
                {
                    ProjectId = ProjectId,
                    IsEnabled = false
                })),
            Assert.ThrowsAsync<RpcException>(() =>
                _service.RemoveConnectionAsync(new RemoveProjectConnectionRequest
                {
                    ProjectId = ProjectId
                }))
        };

        Assert.Multiple(() =>
        {
            Assert.That(exceptions.Select(exception => exception!.StatusCode),
                Is.All.EqualTo(StatusCode.PermissionDenied));
            Assert.That(_validator.Calls, Is.Zero);
            Assert.That(_secrets.CreateCalls, Is.Zero);
        });
    }

    [Test]
    public async Task ConfigureConnection_OwnerValidatesBeforePersistingSecretAndMetadata()
    {
        var reply = await _service.ConfigureConnectionAsync(ConfigureRequest());

        Assert.Multiple(() =>
        {
            Assert.That(_validator.Calls, Is.EqualTo(1));
            Assert.That(_secrets.CreateCalls, Is.EqualTo(1));
            Assert.That(_connections.AddCalls, Is.EqualTo(1));
            Assert.That(_connections.Connection!.SecretName, Is.EqualTo(_secrets.CreatedName));
            Assert.That(reply.Connection.IsEnabled, Is.True);
            Assert.That(reply.Connection.ValidationState,
                Is.EqualTo(ProjectConnectionValidationStateDto.Valid));
        });
    }

    [Test]
    public void ConfigureConnection_InvalidPatCreatesNoSecret()
    {
        _validator.Exception = new AzureDevOpsConnectionValidationException(
            AzureDevOpsConnectionValidationFailure.InvalidCredentials);

        var exception = Assert.ThrowsAsync<RpcException>(() =>
            _service.ConfigureConnectionAsync(ConfigureRequest()));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.StatusCode, Is.EqualTo(StatusCode.InvalidArgument));
            Assert.That(exception.Status.Detail, Does.Not.Contain("valid-pat"));
            Assert.That(_secrets.CreateCalls, Is.Zero);
            Assert.That(_connections.AddCalls, Is.Zero);
        });
    }

    [Test]
    public void ConfigureConnection_MetadataFailureSoftDeletesCreatedSecret()
    {
        _connections.AddException = new ProjectConnectionPersistenceException();

        var exception = Assert.ThrowsAsync<RpcException>(() =>
            _service.ConfigureConnectionAsync(ConfigureRequest()));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.StatusCode, Is.EqualTo(StatusCode.Internal));
            Assert.That(_secrets.CreateCalls, Is.EqualTo(1));
            Assert.That(_secrets.DeletedNames, Has.Count.EqualTo(1));
            Assert.That(_secrets.DeletedNames[0], Is.EqualTo(_secrets.CreatedName));
        });
    }

    [Test]
    public async Task RotateConnection_ValidatesBeforeVersionWriteAndInvalidatesCache()
    {
        _connections.Connection = Connection();

        var reply = await _service.RotateConnectionPatAsync(
            new RotateProjectConnectionPatRequest
            {
                ProjectId = ProjectId,
                PersonalAccessToken = "rotated-pat"
            });

        Assert.Multiple(() =>
        {
            Assert.That(_validator.LastPat, Is.EqualTo("rotated-pat"));
            Assert.That(_secrets.RotatedValues, Is.EqualTo(new[] { "rotated-pat" }));
            Assert.That(_secrets.InvalidatedNames, Does.Contain(_connections.Connection.SecretName));
            Assert.That(reply.Connection.ValidationState,
                Is.EqualTo(ProjectConnectionValidationStateDto.Valid));
            Assert.That(reply.Connection.LastValidatedAtUtc, Is.Not.Null);
        });
    }

    [Test]
    public void UpdateConnection_CannotChangeLockedTarget()
    {
        _connections.Connection = Connection();
        _connections.Connection.TargetLockedAtUtc = DateTimeOffset.UtcNow;
        var request = UpdateRequest();
        request.AzureDevOpsProject = "DifferentProject";

        var exception = Assert.ThrowsAsync<RpcException>(() =>
            _service.UpdateConnectionAsync(request));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.StatusCode, Is.EqualTo(StatusCode.FailedPrecondition));
            Assert.That(_secrets.GetCalls, Is.Zero);
            Assert.That(_validator.Calls, Is.Zero);
            Assert.That(_connections.UpdateCalls, Is.Zero);
        });
    }

    [Test]
    public async Task UpdateConnection_AllowsFieldRotationAfterTargetLock()
    {
        _connections.Connection = Connection();
        _connections.Connection.TargetLockedAtUtc = DateTimeOffset.UtcNow;

        var reply = await _service.UpdateConnectionAsync(UpdateRequest());

        Assert.Multiple(() =>
        {
            Assert.That(_connections.Connection.EstimateField, Is.EqualTo("Custom.Estimate"));
            Assert.That(_connections.UpdateCalls, Is.EqualTo(1));
            Assert.That(_validator.Calls, Is.EqualTo(1));
            Assert.That(reply.Connection.ValidationState,
                Is.EqualTo(ProjectConnectionValidationStateDto.Valid));
        });
    }

    [Test]
    public async Task UpdateConnection_StaleCachedPatEvictsAndRetriesLatestSecretOnce()
    {
        _connections.Connection = Connection();
        _secrets.LatestValues.Enqueue("cached-pat");
        _secrets.LatestValues.Enqueue("latest-pat");
        _validator.FailInvalidCredentialsOnce = true;

        await _service.UpdateConnectionAsync(UpdateRequest());

        Assert.Multiple(() =>
        {
            Assert.That(_validator.Calls, Is.EqualTo(2));
            Assert.That(_validator.ValidatedPats, Is.EqualTo(new[] { "cached-pat", "latest-pat" }));
            Assert.That(_secrets.GetCalls, Is.EqualTo(2));
            Assert.That(_secrets.InvalidatedNames,
                Does.Contain(_connections.Connection.SecretName));
        });
    }

    [Test]
    public async Task SetConnectionEnabled_DisablesConnectionWithoutChangingValidation()
    {
        _connections.Connection = Connection();

        var reply = await _service.SetConnectionEnabledAsync(
            new SetProjectConnectionEnabledRequest
            {
                ProjectId = ProjectId,
                IsEnabled = false
            });

        Assert.Multiple(() =>
        {
            Assert.That(reply.Connection.IsEnabled, Is.False);
            Assert.That(reply.Connection.ValidationState,
                Is.EqualTo(ProjectConnectionValidationStateDto.Valid));
            Assert.That(_connections.UpdateCalls, Is.EqualTo(1));
        });
    }

    [Test]
    public void UpdateConnection_MissingSecretMapsToSanitizedStatus()
    {
        _connections.Connection = Connection();
        _secrets.GetException = new ProjectSecretMissingException();

        var exception = Assert.ThrowsAsync<RpcException>(() =>
            _service.UpdateConnectionAsync(UpdateRequest()));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.StatusCode, Is.EqualTo(StatusCode.FailedPrecondition));
            Assert.That(exception.Status.Detail, Does.Not.Contain("pat-"));
            Assert.That(exception.Status.Detail, Does.Not.Contain("secret-name"));
        });
    }

    [Test]
    public void DeleteProject_WhenSecretCleanupFails_DoesNotDeleteProjectOrInvalidateRooms()
    {
        var sessionId = Guid.NewGuid();
        _connections.Connection = Connection();
        _sessions.Sessions =
        [
            new PlanningSession
            {
                Id = sessionId,
                ProjectId = ProjectId,
                Name = "Session 1"
            }
        ];
        _secrets.SoftDeleteException = new ProjectSecretForbiddenException();

        var exception = Assert.ThrowsAsync<RpcException>(() =>
            _service.DeleteProjectAsync(new DeleteProjectRequest { ProjectId = ProjectId }));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.StatusCode, Is.EqualTo(StatusCode.Unavailable));
            Assert.That(_secrets.DeletedNames, Is.Empty);
            Assert.That(_projects.DeleteCalls, Is.Zero);
            Assert.That(_planningRooms.InvalidatedKeys, Is.Empty);
        });
    }

    [Test]
    public async Task DeleteProject_SoftDeletesSecretBeforeSqlProjectAndThenInvalidatesOwnedRooms()
    {
        var sessionA = Guid.NewGuid();
        var sessionB = Guid.NewGuid();
        _connections.Connection = Connection();
        _sessions.Sessions =
        [
            new PlanningSession { Id = sessionA, ProjectId = ProjectId, Name = "Session A" },
            new PlanningSession { Id = sessionB, ProjectId = ProjectId, Name = "Session B" }
        ];

        await _service.DeleteProjectAsync(
            new DeleteProjectRequest { ProjectId = ProjectId });

        Assert.Multiple(() =>
        {
            Assert.That(_secrets.DeletedNames,
                Is.EqualTo(new[] { _connections.Connection.SecretName }));
            Assert.That(_projects.DeleteCalls, Is.EqualTo(1));
            Assert.That(_planningRooms.InvalidatedKeys.Select(key => key.SessionId),
                Is.EquivalentTo(new[] { sessionA, sessionB }));
        });
    }

    [Test]
    public void ConnectionResponseSerialization_DoesNotExposePatOrSecretIdentifier()
    {
        const string pat = "sensitive-personal-access-token";
        const string secretName = "pat-0123456789abcdef0123456789abcdef";
        var response = new ProjectConnectionReply
        {
            Connection = new ProjectConnectionDto
            {
                IsEnabled = true,
                ValidationState = ProjectConnectionValidationStateDto.Valid,
                LastValidatedAtUtc = DateTime.UtcNow
            }
        };

        var json = JsonSerializer.Serialize(response);
        var text = response.ToString()!;

        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Not.Contain(pat));
            Assert.That(json, Does.Not.Contain(secretName));
            Assert.That(json, Does.Not.Contain("PersonalAccessToken"));
            Assert.That(json, Does.Not.Contain("SecretName"));
            Assert.That(text, Does.Not.Contain(pat));
            Assert.That(text, Does.Not.Contain(secretName));
            Assert.That(text, Does.Not.Contain("SecretName"));
        });
    }

    private static ConfigureProjectConnectionRequest ConfigureRequest() => new()
    {
        ProjectId = ProjectId,
        OrganizationUrl = "https://dev.azure.com/contoso",
        AzureDevOpsProject = "PlanDeck",
        EstimateField = "Microsoft.VSTS.Scheduling.StoryPoints",
        DescriptionField = "System.Description",
        ReproStepsField = "Microsoft.VSTS.TCM.ReproSteps",
        AcceptanceCriteriaField = "Microsoft.VSTS.Common.AcceptanceCriteria",
        PersonalAccessToken = "valid-pat"
    };

    private static UpdateProjectConnectionRequest UpdateRequest() => new()
    {
        ProjectId = ProjectId,
        OrganizationUrl = "https://dev.azure.com/contoso",
        AzureDevOpsProject = "PlanDeck",
        EstimateField = "Custom.Estimate",
        DescriptionField = "System.Description",
        ReproStepsField = "Microsoft.VSTS.TCM.ReproSteps",
        AcceptanceCriteriaField = "Microsoft.VSTS.Common.AcceptanceCriteria"
    };

    private static ProjectAzureDevOpsConnection Connection() => new()
    {
        ProjectId = ProjectId,
        OrganizationUrl = "https://dev.azure.com/contoso",
        AzureDevOpsProject = "PlanDeck",
        EstimateField = "Microsoft.VSTS.Scheduling.StoryPoints",
        DescriptionField = "System.Description",
        ReproStepsField = "Microsoft.VSTS.TCM.ReproSteps",
        AcceptanceCriteriaField = "Microsoft.VSTS.Common.AcceptanceCriteria",
        SecretName = "pat-secret-name",
        IsEnabled = true,
        ValidationState = ConnectionValidationState.Valid,
        LastValidatedAtUtc = DateTimeOffset.UtcNow
    };

    private sealed class RoleAccessResolver(ProjectRole role) : IProjectAccessResolver
    {
        public ProjectRole Role { get; set; } = role;

        public Task<ProjectRole?> GetEffectiveRoleAsync(
            Guid projectId,
            CancellationToken cancellationToken) =>
            Task.FromResult<ProjectRole?>(Role);

        public Task<ProjectRole> RequireRoleAsync(
            Guid projectId,
            ProjectRole minimumRole,
            CancellationToken cancellationToken)
        {
            if (Role < minimumRole)
            {
                throw new ProjectPermissionDeniedException(projectId, minimumRole);
            }

            return Task.FromResult(Role);
        }
    }

    private sealed class StubConnectionValidator : IAzureDevOpsConnectionValidator
    {
        public int Calls { get; private set; }

        public string? LastPat { get; private set; }

        public Exception? Exception { get; set; }

        public bool FailInvalidCredentialsOnce { get; set; }

        public List<string> ValidatedPats { get; } = [];

        public Task ValidateAsync(
            AzureDevOpsConnectionValidationRequest request,
            CancellationToken cancellationToken)
        {
            Calls++;
            LastPat = request.PersonalAccessToken;
            ValidatedPats.Add(request.PersonalAccessToken);
            if (FailInvalidCredentialsOnce && Calls == 1)
            {
                throw new AzureDevOpsConnectionValidationException(
                    AzureDevOpsConnectionValidationFailure.InvalidCredentials);
            }

            if (Exception is not null)
            {
                throw Exception;
            }

            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryProjectSecretStore : IProjectSecretStore
    {
        public string CreatedName { get; } = $"pat-{Guid.NewGuid():N}";

        public int CreateCalls { get; private set; }

        public int GetCalls { get; private set; }

        public Exception? GetException { get; set; }

        public Queue<string> LatestValues { get; } = [];

        public List<string> DeletedNames { get; } = [];

        public List<string> RotatedValues { get; } = [];

        public List<string> InvalidatedNames { get; } = [];

        public Exception? SoftDeleteException { get; set; }

        public Task<string> CreateAsync(string value, CancellationToken cancellationToken)
        {
            CreateCalls++;
            return Task.FromResult(CreatedName);
        }

        public Task<string> GetLatestAsync(
            string secretName,
            CancellationToken cancellationToken)
        {
            GetCalls++;
            if (GetException is not null)
            {
                throw GetException;
            }

            return Task.FromResult(
                LatestValues.TryDequeue(out var value) ? value : "current-pat");
        }

        public Task RotateAsync(
            string secretName,
            string value,
            CancellationToken cancellationToken)
        {
            RotatedValues.Add(value);
            return Task.CompletedTask;
        }

        public Task SoftDeleteAsync(
            string secretName,
            CancellationToken cancellationToken)
        {
            if (SoftDeleteException is not null)
            {
                throw SoftDeleteException;
            }

            DeletedNames.Add(secretName);
            return Task.CompletedTask;
        }

        public void Invalidate(string secretName) => InvalidatedNames.Add(secretName);
    }

    private sealed class StubConnectionRepository
        : IProjectAzureDevOpsConnectionRepository
    {
        public ProjectAzureDevOpsConnection? Connection { get; set; }

        public Exception? AddException { get; set; }

        public int AddCalls { get; private set; }

        public int UpdateCalls { get; private set; }

        public Task<ProjectAzureDevOpsConnection?> GetAsync(
            Guid projectId,
            CancellationToken cancellationToken) =>
            Task.FromResult(Connection);

        public Task AddAsync(
            ProjectAzureDevOpsConnection connection,
            CancellationToken cancellationToken)
        {
            AddCalls++;
            if (AddException is not null)
            {
                throw AddException;
            }

            Connection = connection;
            return Task.CompletedTask;
        }

        public Task UpdateAsync(
            ProjectAzureDevOpsConnection connection,
            CancellationToken cancellationToken)
        {
            UpdateCalls++;
            Connection = connection;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(
            ProjectAzureDevOpsConnection connection,
            CancellationToken cancellationToken)
        {
            Connection = null;
            return Task.CompletedTask;
        }

        public Task LockTargetAsync(
            Guid projectId,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class StubProjectRepository : IProjectRepository
    {
        public Exception? DeleteException { get; set; }

        public int DeleteCalls { get; private set; }

        public Task<PlanDeckProject> CreateAsync(
            string name,
            string? description,
            string ownerEmail,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<PlanDeckProject>> ListAccessibleAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PlanDeckProject>>([]);

        public Task<PlanDeckProject?> GetAsync(
            Guid projectId,
            CancellationToken cancellationToken) =>
            Task.FromResult<PlanDeckProject?>(null);

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
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task RemoveMemberAsync(
            Guid projectId,
            Guid memberId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ProjectMember> ChangeMemberRoleAsync(
            Guid projectId,
            Guid memberId,
            ProjectRole role,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ProjectTeam> AssignTeamAsync(
            Guid projectId,
            Guid teamId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task UnassignTeamAsync(
            Guid projectId,
            Guid teamId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task TransferOwnershipAsync(
            Guid projectId,
            Guid newOwnerMemberId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task DeleteAsync(Guid projectId, CancellationToken cancellationToken)
        {
            if (DeleteException is not null)
            {
                throw DeleteException;
            }

            DeleteCalls++;
            return Task.CompletedTask;
        }
    }

    private sealed class StubSessionRepository : ISessionRepository
    {
        public IReadOnlyList<PlanningSession> Sessions { get; set; } = [];

        public Task<PlanningSession> CreateSessionAsync(
            PlanningSession session,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<PlanningSession>> GetSessionsAsync(
            Guid projectId,
            CancellationToken cancellationToken) =>
            Task.FromResult(Sessions.Where(session => session.ProjectId == projectId).ToList() as IReadOnlyList<PlanningSession>);

        public Task<PlanningSession?> GetSessionAsync(Guid id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

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

    private sealed class RecordingPlanningRoomService : IPlanningRoomService
    {
        public List<RoomKey> InvalidatedKeys { get; } = [];

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

        public bool InvalidateSession(RoomKey key)
        {
            InvalidatedKeys.Add(key);
            return true;
        }

        public int RemoveInactiveRooms(DateTimeOffset inactiveSince) =>
            throw new NotSupportedException();
    }

    private sealed class StubCurrentUserContext : ICurrentUserContext
    {
        public Guid TenantId { get; } = Guid.NewGuid();

        public Guid UserId { get; } = Guid.NewGuid();

        public bool IsAuthenticated => true;

        public string? DisplayName => "Owner";

        public string? Email => "owner@example.com";
    }
}
