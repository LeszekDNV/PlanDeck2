using Grpc.Core;
using PlanDeck.Application.Abstractions;
using PlanDeck.Application.Domain;
using PlanDeck.Application.Planning;
using PlanDeck.Application.Services;
using PlanDeck.Core.Shared.Contracts;

namespace PlanDeck.Unit.Tests.Sessions;

[TestFixture]
public sealed class SessionGrpcServiceTests
{
    private static readonly Guid ProjectId = Guid.NewGuid();
    private FakeSessionRepository _repository = null!;
    private FakeSessionMemberRepository _memberRepository = null!;
    private FakeCurrentUserContext _currentUser = null!;
    private RecordingPlanningRoomNotifier _notifier = null!;
    private StubShareCodeGenerator _shareCodeGenerator = null!;
    private FakeAzureDevOpsWorkItemClient _azureDevOpsClient = null!;
    private SessionGrpcService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _repository = new FakeSessionRepository();
        _memberRepository = new FakeSessionMemberRepository();
        _currentUser = new FakeCurrentUserContext();
        _notifier = new RecordingPlanningRoomNotifier();
        _shareCodeGenerator = new StubShareCodeGenerator();
        _azureDevOpsClient = new FakeAzureDevOpsWorkItemClient();
        _service = new SessionGrpcService(_repository, _memberRepository, _currentUser, _notifier, _shareCodeGenerator, _azureDevOpsClient);
    }

    [Test]
    public async Task CreateSession_WithFibonacciScale_ResolvesCanonicalFaces()
    {
        var request = new CreateSessionRequest
        {
            Name = "Sprint 1",
            ProjectId = ProjectId,
            ScaleType = VotingScaleTypeDto.Fibonacci
        };

        var reply = await _service.CreateSessionAsync(request);

        Assert.That(reply.Session.ScaleValues, Is.EqualTo(new[] { "0", "1", "2", "3", "5", "8", "13", "21", "?", "☕" }));
        Assert.That(reply.Session.Status, Is.EqualTo(SessionStatusDto.Draft));
    }

    [Test]
    public async Task CreateSession_WithTShirtScale_ResolvesCanonicalFaces()
    {
        var request = new CreateSessionRequest
        {
            Name = "Sprint 1",
            ProjectId = ProjectId,
            ScaleType = VotingScaleTypeDto.TShirt
        };

        var reply = await _service.CreateSessionAsync(request);

        Assert.That(reply.Session.ScaleValues, Is.EqualTo(new[] { "XS", "S", "M", "L", "XL", "?", "☕" }));
    }

    [Test]
    public async Task CreateSession_WithCustomScale_TrimsAndDedupes()
    {
        var request = new CreateSessionRequest
        {
            Name = "Sprint 1",
            ProjectId = ProjectId,
            ScaleType = VotingScaleTypeDto.Custom,
            CustomScaleValues = [" 1 ", "2", "2", "", "  ", "3"]
        };

        var reply = await _service.CreateSessionAsync(request);

        Assert.That(reply.Session.ScaleValues, Is.EqualTo(new[] { "1", "2", "3" }));
    }

    [Test]
    public void CreateSession_WithBlankName_ThrowsInvalidArgument()
    {
        var request = new CreateSessionRequest
        {
            Name = "   ",
            ScaleType = VotingScaleTypeDto.Fibonacci
        };

        var ex = Assert.ThrowsAsync<RpcException>(() => _service.CreateSessionAsync(request));
        Assert.That(ex!.StatusCode, Is.EqualTo(StatusCode.InvalidArgument));
    }

    [Test]
    public void CreateSession_WithoutProjectId_ThrowsInvalidArgument()
    {
        var request = new CreateSessionRequest
        {
            Name = "Sprint 1",
            ScaleType = VotingScaleTypeDto.Fibonacci
        };

        var ex = Assert.ThrowsAsync<RpcException>(() => _service.CreateSessionAsync(request));

        Assert.That(ex!.StatusCode, Is.EqualTo(StatusCode.InvalidArgument));
        Assert.That(ex.Status.Detail, Does.Contain("ProjectId"));
    }

    [Test]
    public void CreateSession_WithEmptyCustomScale_ThrowsInvalidArgument()
    {
        var request = new CreateSessionRequest
        {
            Name = "Sprint 1",
            ScaleType = VotingScaleTypeDto.Custom,
            CustomScaleValues = ["  ", ""]
        };

        var ex = Assert.ThrowsAsync<RpcException>(() => _service.CreateSessionAsync(request));
        Assert.That(ex!.StatusCode, Is.EqualTo(StatusCode.InvalidArgument));
    }

    [Test]
    public void CreateSession_WithBlankTaskTitle_ThrowsInvalidArgument()
    {
        var request = new CreateSessionRequest
        {
            Name = "Sprint 1",
            ProjectId = ProjectId,
            ScaleType = VotingScaleTypeDto.Fibonacci,
            Tasks = [new NewSessionTaskDto { Title = "  " }]
        };

        var ex = Assert.ThrowsAsync<RpcException>(() => _service.CreateSessionAsync(request));
        Assert.That(ex!.StatusCode, Is.EqualTo(StatusCode.InvalidArgument));
    }

    [Test]
    public async Task CreateSession_WithTasks_AssignsSequentialSortOrder()
    {
        var request = new CreateSessionRequest
        {
            Name = "Sprint 1",
            ProjectId = ProjectId,
            ScaleType = VotingScaleTypeDto.Fibonacci,
            Tasks =
            [
                new NewSessionTaskDto { Title = "First" },
                new NewSessionTaskDto { Title = "Second" }
            ]
        };

        var reply = await _service.CreateSessionAsync(request);

        Assert.That(reply.Session.Tasks.Select(t => t.SortOrder), Is.EqualTo(new[] { 0, 1 }));
        Assert.That(reply.Session.Tasks.Select(t => t.Title), Is.EqualTo(new[] { "First", "Second" }));
    }

    [Test]
    public async Task CreateSession_WithDuplicateAdoWorkItems_KeepsFirstOnly()
    {
        var request = new CreateSessionRequest
        {
            Name = "Sprint 1",
            ProjectId = ProjectId,
            ScaleType = VotingScaleTypeDto.Fibonacci,
            Tasks =
            [
                new NewSessionTaskDto { Title = "Item 101", Source = TaskSourceDto.AzureDevOps, AdoWorkItemId = 101 },
                new NewSessionTaskDto { Title = "Item 101 again", Source = TaskSourceDto.AzureDevOps, AdoWorkItemId = 101 },
                new NewSessionTaskDto { Title = "Item 102", Source = TaskSourceDto.AzureDevOps, AdoWorkItemId = 102 }
            ]
        };

        var reply = await _service.CreateSessionAsync(request);

        Assert.That(reply.Session.Tasks.Select(t => t.AdoWorkItemId), Is.EqualTo(new int?[] { 101, 102 }));
        Assert.That(reply.Session.Tasks.Select(t => t.SortOrder), Is.EqualTo(new[] { 0, 1 }));
    }

    [Test]
    public async Task AddTask_WithExistingAdoWorkItem_IsIdempotent()
    {
        var session = _repository.Seed(SessionStatus.Draft);
        var request = new AddTaskRequest
        {
            SessionId = session.Id,
            Task = new NewSessionTaskDto { Title = "Item 101", Source = TaskSourceDto.AzureDevOps, AdoWorkItemId = 101 }
        };

        await _service.AddTaskAsync(request);
        var reply = await _service.AddTaskAsync(request);

        Assert.That(reply.Session.Tasks.Count(t => t.AdoWorkItemId == 101), Is.EqualTo(1));
    }

    [Test]
    public void UpdateSessionConfig_OnActiveSession_ThrowsFailedPrecondition()
    {
        var session = _repository.Seed(SessionStatus.Active);

        var request = new UpdateSessionConfigRequest
        {
            Id = session.Id,
            Name = "Renamed",
            ScaleType = VotingScaleTypeDto.Fibonacci
        };

        var ex = Assert.ThrowsAsync<RpcException>(() => _service.UpdateSessionConfigAsync(request));
        Assert.That(ex!.StatusCode, Is.EqualTo(StatusCode.FailedPrecondition));
    }

    [Test]
    public async Task AddTask_OnActiveSession_IsAllowedAndNotifiesRoom()
    {
        var session = _repository.Seed(SessionStatus.Active);

        var request = new AddTaskRequest
        {
            SessionId = session.Id,
            Task = new NewSessionTaskDto { Title = "Late task" }
        };

        var reply = await _service.AddTaskAsync(request);

        Assert.That(reply.Session.Tasks.Select(t => t.Title), Does.Contain("Late task"));
        Assert.That(_notifier.Calls, Has.Count.EqualTo(1));
        Assert.That(_notifier.LastSessionId, Is.EqualTo(session.Id));
        Assert.That(_notifier.LastTasks.Select(t => t.Title), Does.Contain("Late task"));
    }

    [Test]
    public async Task AddTask_OnDraftSession_DoesNotNotifyRoom()
    {
        var session = _repository.Seed(SessionStatus.Draft);

        await _service.AddTaskAsync(new AddTaskRequest
        {
            SessionId = session.Id,
            Task = new NewSessionTaskDto { Title = "Draft task" }
        });

        Assert.That(_notifier.Calls, Is.Empty);
    }

    [Test]
    public async Task UpdateTask_SetsTitleAndDescription()
    {
        var session = _repository.Seed(SessionStatus.Draft);
        await _service.AddTaskAsync(new AddTaskRequest
        {
            SessionId = session.Id,
            Task = new NewSessionTaskDto { Title = "Original" }
        });
        var taskId = session.Tasks.Single().Id;

        var reply = await _service.UpdateTaskAsync(new UpdateTaskRequest
        {
            SessionId = session.Id,
            TaskId = taskId,
            Title = "  Renamed  ",
            Description = "  ## Notes  "
        });

        var task = reply.Session.Tasks.Single();
        Assert.That(task.Title, Is.EqualTo("Renamed"));
        Assert.That(task.Description, Is.EqualTo("## Notes"));
    }

    [Test]
    public async Task UpdateTask_WithBlankDescription_StoresNull()
    {
        var session = _repository.Seed(SessionStatus.Draft);
        await _service.AddTaskAsync(new AddTaskRequest
        {
            SessionId = session.Id,
            Task = new NewSessionTaskDto { Title = "Original", Description = "old" }
        });
        var taskId = session.Tasks.Single().Id;

        var reply = await _service.UpdateTaskAsync(new UpdateTaskRequest
        {
            SessionId = session.Id,
            TaskId = taskId,
            Title = "Kept",
            Description = "   "
        });

        Assert.That(reply.Session.Tasks.Single().Description, Is.Null);
    }

    [Test]
    public void UpdateTask_WithBlankTitle_ThrowsInvalidArgument()
    {
        var session = _repository.Seed(SessionStatus.Draft);

        var ex = Assert.ThrowsAsync<RpcException>(() => _service.UpdateTaskAsync(new UpdateTaskRequest
        {
            SessionId = session.Id,
            TaskId = Guid.NewGuid(),
            Title = "   "
        }));
        Assert.That(ex!.StatusCode, Is.EqualTo(StatusCode.InvalidArgument));
    }

    [Test]
    public void UpdateTask_WithUnknownTask_ThrowsNotFound()
    {
        var session = _repository.Seed(SessionStatus.Draft);

        var ex = Assert.ThrowsAsync<RpcException>(() => _service.UpdateTaskAsync(new UpdateTaskRequest
        {
            SessionId = session.Id,
            TaskId = Guid.NewGuid(),
            Title = "Renamed"
        }));
        Assert.That(ex!.StatusCode, Is.EqualTo(StatusCode.NotFound));
    }

    [Test]
    public async Task UpdateTask_OnActiveSession_IsAllowedAndNotifiesRoom()
    {
        var session = _repository.Seed(SessionStatus.Active);
        session.Tasks.Add(new SessionTask { Title = "Original", SortOrder = 0 });
        var taskId = session.Tasks.Single().Id;

        var reply = await _service.UpdateTaskAsync(new UpdateTaskRequest
        {
            SessionId = session.Id,
            TaskId = taskId,
            Title = "Renamed",
            Description = "desc"
        });

        Assert.That(reply.Session.Tasks.Single().Title, Is.EqualTo("Renamed"));
        Assert.That(_notifier.Calls, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task AddTasks_AppendsWithSortContinuationAndDedupesAdo()
    {
        var session = _repository.Seed(SessionStatus.Draft);
        session.Tasks.Add(new SessionTask { Title = "Existing", SortOrder = 0, Source = TaskSource.AdHoc });

        var reply = await _service.AddTasksAsync(new AddTasksRequest
        {
            SessionId = session.Id,
            Tasks =
            [
                new NewSessionTaskDto { Title = "Bulk A" },
                new NewSessionTaskDto { Title = "Bulk B", Source = TaskSourceDto.AzureDevOps, AdoWorkItemId = 55 },
                new NewSessionTaskDto { Title = "Bulk B dup", Source = TaskSourceDto.AzureDevOps, AdoWorkItemId = 55 }
            ]
        });

        Assert.That(reply.Session.Tasks.Select(t => t.Title), Is.EqualTo(new[] { "Existing", "Bulk A", "Bulk B" }));
        Assert.That(reply.Session.Tasks.Select(t => t.SortOrder), Is.EqualTo(new[] { 0, 1, 2 }));
    }

    [Test]
    public async Task AddTasks_OnActiveSession_NotifiesRoomOnce()
    {
        var session = _repository.Seed(SessionStatus.Active);

        await _service.AddTasksAsync(new AddTasksRequest
        {
            SessionId = session.Id,
            Tasks =
            [
                new NewSessionTaskDto { Title = "A" },
                new NewSessionTaskDto { Title = "B" }
            ]
        });

        Assert.That(_notifier.Calls, Has.Count.EqualTo(1));
        Assert.That(_notifier.LastTasks.Select(t => t.Title), Is.EqualTo(new[] { "A", "B" }));
    }

    [Test]
    public void UpdateSessionConfig_OnMissingSession_ThrowsNotFound()
    {
        var request = new UpdateSessionConfigRequest
        {
            Id = Guid.NewGuid(),
            Name = "Renamed",
            ScaleType = VotingScaleTypeDto.Fibonacci
        };

        var ex = Assert.ThrowsAsync<RpcException>(() => _service.UpdateSessionConfigAsync(request));
        Assert.That(ex!.StatusCode, Is.EqualTo(StatusCode.NotFound));
    }

    [Test]
    public async Task ActivateSession_FlipsDraftToActive()
    {
        var session = _repository.Seed(SessionStatus.Draft);

        var reply = await _service.ActivateSessionAsync(new ActivateSessionRequest { Id = session.Id });

        Assert.That(reply.Session.Status, Is.EqualTo(SessionStatusDto.Active));
        Assert.That(session.Status, Is.EqualTo(SessionStatus.Active));
    }

    [Test]
    public async Task ActivateSession_AssignsShareCode()
    {
        var session = _repository.Seed(SessionStatus.Draft);

        var reply = await _service.ActivateSessionAsync(new ActivateSessionRequest { Id = session.Id });

        Assert.That(reply.Session.ShareCode, Is.Not.Null.And.Not.Empty);
        Assert.That(session.ShareCode, Is.EqualTo(reply.Session.ShareCode));
        Assert.That(_shareCodeGenerator.CallCount, Is.EqualTo(1));
    }

    [Test]
    public async Task ActivateSession_WhenAlreadyActive_KeepsExistingShareCode()
    {
        var session = _repository.Seed(SessionStatus.Draft);
        await _service.ActivateSessionAsync(new ActivateSessionRequest { Id = session.Id });
        var firstCode = session.ShareCode;

        var reply = await _service.ActivateSessionAsync(new ActivateSessionRequest { Id = session.Id });

        Assert.That(reply.Session.ShareCode, Is.EqualTo(firstCode));
        Assert.That(_shareCodeGenerator.CallCount, Is.EqualTo(1));
    }

    [Test]
    public async Task CreateSession_AddsCreatorAsMember()
    {
        _currentUser.Email = "creator@example.com";
        _currentUser.DisplayName = "Creator";

        var reply = await _service.CreateSessionAsync(new CreateSessionRequest
        {
            Name = "Sprint 1",
            ProjectId = ProjectId,
            ScaleType = VotingScaleTypeDto.Fibonacci
        });

        Assert.That(_memberRepository.Members, Has.Count.EqualTo(1));
        var member = _memberRepository.Members[0];
        Assert.That(member.SessionId, Is.EqualTo(reply.Session.Id));
        Assert.That(member.Email, Is.EqualTo("creator@example.com"));
        Assert.That(member.DisplayName, Is.EqualTo("Creator"));
    }

    [Test]
    public async Task CreateSession_WithoutEmail_DoesNotAddMember()
    {
        _currentUser.Email = null;

        await _service.CreateSessionAsync(new CreateSessionRequest
        {
            Name = "Sprint 1",
            ProjectId = ProjectId,
            ScaleType = VotingScaleTypeDto.Fibonacci
        });

        Assert.That(_memberRepository.Members, Is.Empty);
    }

    [Test]
    public async Task CreateSession_WhenCreatorAlreadyMember_SwallowsDuplicate()
    {
        _currentUser.Email = "creator@example.com";
        _memberRepository.ThrowDuplicateOnce = true;

        Assert.DoesNotThrowAsync(() => _service.CreateSessionAsync(new CreateSessionRequest
        {
            Name = "Sprint 1",
            ProjectId = ProjectId,
            ScaleType = VotingScaleTypeDto.Fibonacci
        }));

        await Task.CompletedTask;
    }

    [Test]
    public void ListSessions_AsGuest_ThrowsPermissionDenied()
    {
        _currentUser.IsGuest = true;

        var ex = Assert.ThrowsAsync<RpcException>(() => _service.ListSessionsAsync(new ListSessionsRequest()));
        Assert.That(ex!.StatusCode, Is.EqualTo(StatusCode.PermissionDenied));
    }

    [Test]
    public void GetSession_AsGuest_ForForeignSession_ThrowsPermissionDenied()
    {
        _currentUser.IsGuest = true;
        _currentUser.SessionScope = Guid.NewGuid();

        var ex = Assert.ThrowsAsync<RpcException>(
            () => _service.GetSessionAsync(new GetSessionRequest { Id = Guid.NewGuid() }));
        Assert.That(ex!.StatusCode, Is.EqualTo(StatusCode.PermissionDenied));
    }

    [Test]
    public async Task GetSession_AsGuest_ForOwnSession_IsAllowed()
    {
        var created = await _service.CreateSessionAsync(new CreateSessionRequest
        {
            Name = "Sprint 1",
            ProjectId = ProjectId,
            ScaleType = VotingScaleTypeDto.Fibonacci
        });
        var sessionId = created.Session.Id;

        _currentUser.IsGuest = true;
        _currentUser.SessionScope = sessionId;

        var reply = await _service.GetSessionAsync(new GetSessionRequest { Id = sessionId });

        Assert.That(reply.Session.Id, Is.EqualTo(sessionId));
    }

    private SessionTask SeedAdoTask(PlanningSession session, string? agreedEstimate, int? adoWorkItemId = 1001, int? adoRevision = 4, TaskSource source = TaskSource.AzureDevOps)
    {
        var task = new SessionTask
        {
            SessionId = session.Id,
            Title = "Imported work item",
            Source = source,
            AdoWorkItemId = adoWorkItemId,
            AdoRevision = adoRevision,
            AgreedEstimate = agreedEstimate
        };
        session.Tasks.Add(task);
        return task;
    }

    [Test]
    public async Task WriteTaskEstimateToAdo_HappyPath_ForwardsRequestAndPersistsRevision()
    {
        var session = _repository.Seed(SessionStatus.Active);
        var task = SeedAdoTask(session, "8", adoWorkItemId: 1001, adoRevision: 4);

        var reply = await _service.WriteTaskEstimateToAdoAsync(new WriteTaskEstimateRequest
        {
            SessionId = session.Id,
            TaskId = task.Id
        });

        Assert.That(_azureDevOpsClient.LastWriteRequest, Is.Not.Null);
        Assert.That(_azureDevOpsClient.LastWriteRequest!.WorkItemId, Is.EqualTo(1001));
        Assert.That(_azureDevOpsClient.LastWriteRequest!.ExpectedRevision, Is.EqualTo(4));
        Assert.That(_azureDevOpsClient.LastWriteRequest!.Estimate, Is.EqualTo(8d));
        Assert.That(reply.WorkItemId, Is.EqualTo(1001));
        Assert.That(reply.Revision, Is.EqualTo(5));
        Assert.That(_repository.LastAdoRevisionSet, Is.EqualTo(5));
        Assert.That(task.AdoRevision, Is.EqualTo(5));
    }

    [Test]
    public void WriteTaskEstimateToAdo_WhenTaskMissing_ThrowsNotFound()
    {
        var session = _repository.Seed(SessionStatus.Active);

        var ex = Assert.ThrowsAsync<RpcException>(() => _service.WriteTaskEstimateToAdoAsync(new WriteTaskEstimateRequest
        {
            SessionId = session.Id,
            TaskId = Guid.NewGuid()
        }));
        Assert.That(ex!.StatusCode, Is.EqualTo(StatusCode.NotFound));
    }

    [Test]
    public void WriteTaskEstimateToAdo_WhenSessionMissing_ThrowsNotFound()
    {
        var ex = Assert.ThrowsAsync<RpcException>(() => _service.WriteTaskEstimateToAdoAsync(new WriteTaskEstimateRequest
        {
            SessionId = Guid.NewGuid(),
            TaskId = Guid.NewGuid()
        }));
        Assert.That(ex!.StatusCode, Is.EqualTo(StatusCode.NotFound));
    }

    [Test]
    public void WriteTaskEstimateToAdo_WhenNotAdoTask_ThrowsFailedPrecondition()
    {
        var session = _repository.Seed(SessionStatus.Active);
        var task = SeedAdoTask(session, "8", adoWorkItemId: null, source: TaskSource.AdHoc);

        var ex = Assert.ThrowsAsync<RpcException>(() => _service.WriteTaskEstimateToAdoAsync(new WriteTaskEstimateRequest
        {
            SessionId = session.Id,
            TaskId = task.Id
        }));
        Assert.That(ex!.StatusCode, Is.EqualTo(StatusCode.FailedPrecondition));
    }

    [Test]
    public void WriteTaskEstimateToAdo_WhenNoAgreedEstimate_ThrowsFailedPrecondition()
    {
        var session = _repository.Seed(SessionStatus.Active);
        var task = SeedAdoTask(session, agreedEstimate: null);

        var ex = Assert.ThrowsAsync<RpcException>(() => _service.WriteTaskEstimateToAdoAsync(new WriteTaskEstimateRequest
        {
            SessionId = session.Id,
            TaskId = task.Id
        }));
        Assert.That(ex!.StatusCode, Is.EqualTo(StatusCode.FailedPrecondition));
    }

    [Test]
    public void WriteTaskEstimateToAdo_WhenEstimateNotNumeric_ThrowsFailedPrecondition()
    {
        var session = _repository.Seed(SessionStatus.Active);
        var task = SeedAdoTask(session, "XL");

        var ex = Assert.ThrowsAsync<RpcException>(() => _service.WriteTaskEstimateToAdoAsync(new WriteTaskEstimateRequest
        {
            SessionId = session.Id,
            TaskId = task.Id
        }));
        Assert.That(ex!.StatusCode, Is.EqualTo(StatusCode.FailedPrecondition));
    }

    [Test]
    public void WriteTaskEstimateToAdo_AsGuest_ThrowsPermissionDenied()
    {
        var session = _repository.Seed(SessionStatus.Active);
        var task = SeedAdoTask(session, "8");
        _currentUser.IsGuest = true;

        var ex = Assert.ThrowsAsync<RpcException>(() => _service.WriteTaskEstimateToAdoAsync(new WriteTaskEstimateRequest
        {
            SessionId = session.Id,
            TaskId = task.Id
        }));
        Assert.That(ex!.StatusCode, Is.EqualTo(StatusCode.PermissionDenied));
    }

    [Test]
    public void WriteTaskEstimateToAdo_OnConcurrencyConflict_ThrowsAborted()
    {
        var session = _repository.Seed(SessionStatus.Active);
        var task = SeedAdoTask(session, "8");
        _azureDevOpsClient.OnWrite = _ => throw new AzureDevOpsConcurrencyException("revision changed");

        var ex = Assert.ThrowsAsync<RpcException>(() => _service.WriteTaskEstimateToAdoAsync(new WriteTaskEstimateRequest
        {
            SessionId = session.Id,
            TaskId = task.Id
        }));
        Assert.That(ex!.StatusCode, Is.EqualTo(StatusCode.Aborted));
    }

    [Test]
    public void WriteTaskEstimateToAdo_OnRateLimit_ThrowsResourceExhausted()
    {
        var session = _repository.Seed(SessionStatus.Active);
        var task = SeedAdoTask(session, "8");
        _azureDevOpsClient.OnWrite = _ => throw new AzureDevOpsRateLimitException("rate limited");

        var ex = Assert.ThrowsAsync<RpcException>(() => _service.WriteTaskEstimateToAdoAsync(new WriteTaskEstimateRequest
        {
            SessionId = session.Id,
            TaskId = task.Id
        }));
        Assert.That(ex!.StatusCode, Is.EqualTo(StatusCode.ResourceExhausted));
    }

    [Test]
    public void WriteTaskEstimateToAdo_OnGenericFailure_ThrowsUnavailable()
    {
        var session = _repository.Seed(SessionStatus.Active);
        var task = SeedAdoTask(session, "8");
        _azureDevOpsClient.OnWrite = _ => throw new HttpRequestException("boom");

        var ex = Assert.ThrowsAsync<RpcException>(() => _service.WriteTaskEstimateToAdoAsync(new WriteTaskEstimateRequest
        {
            SessionId = session.Id,
            TaskId = task.Id
        }));
        Assert.That(ex!.StatusCode, Is.EqualTo(StatusCode.Unavailable));
    }

    private sealed class StubShareCodeGenerator : IShareCodeGenerator
    {
        public int CallCount { get; private set; }

        public string Generate()
        {
            CallCount++;
            return $"CODE{CallCount:D6}";
        }
    }

    private sealed class FakeCurrentUserContext : ICurrentUserContext
    {
        public Guid TenantId { get; } = Guid.NewGuid();
        public Guid UserId { get; } = Guid.NewGuid();
        public bool IsAuthenticated { get; set; } = true;
        public string? DisplayName { get; set; }
        public string? Email { get; set; } = "creator@example.com";
        public bool IsGuest { get; set; }
        public Guid? SessionScope { get; set; }
    }

    private sealed class FakeSessionMemberRepository : ISessionMemberRepository
    {
        public List<SessionMember> Members { get; } = [];
        public bool ThrowDuplicateOnce { get; set; }

        public Task<SessionMember> AssignMemberAsync(Guid sessionId, string email, string? displayName, CancellationToken cancellationToken)
        {
            if (ThrowDuplicateOnce)
            {
                ThrowDuplicateOnce = false;
                throw new DuplicateSessionMemberException(sessionId, email);
            }

            var member = new SessionMember { SessionId = sessionId, Email = email, DisplayName = displayName };
            Members.Add(member);
            return Task.FromResult(member);
        }

        public Task<bool> RemoveMemberAsync(Guid sessionId, Guid memberId, CancellationToken cancellationToken)
            => Task.FromResult(false);

        public Task<IReadOnlyList<SessionMember>> GetMembersAsync(Guid sessionId, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<SessionMember>>(Members);
    }

    private sealed class RecordingPlanningRoomNotifier : IPlanningRoomNotifier
    {
        public List<Guid> Calls { get; } = [];
        public Guid LastSessionId { get; private set; }
        public IReadOnlyList<PlanningRoomTaskSnapshot> LastTasks { get; private set; } = [];

        public Task NotifyTasksChangedAsync(
            Guid sessionId,
            IReadOnlyList<PlanningRoomTaskSnapshot> tasks,
            CancellationToken cancellationToken)
        {
            Calls.Add(sessionId);
            LastSessionId = sessionId;
            LastTasks = tasks;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeSessionRepository : ISessionRepository
    {
        private readonly List<PlanningSession> _sessions = [];

        public PlanningSession Seed(SessionStatus status)
        {
            var session = new PlanningSession
            {
                Name = "Seeded",
                Status = status,
                ScaleType = VotingScaleType.Fibonacci,
                ScaleValues = ["1", "2", "3"]
            };
            _sessions.Add(session);
            return session;
        }

        public Task<PlanningSession> CreateSessionAsync(PlanningSession session, CancellationToken cancellationToken)
        {
            _sessions.Add(session);
            return Task.FromResult(session);
        }

        public Task<IReadOnlyList<PlanningSession>> GetSessionsAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<PlanningSession>>(_sessions);

        public Task<PlanningSession?> GetSessionAsync(Guid id, CancellationToken cancellationToken)
            => Task.FromResult(_sessions.FirstOrDefault(s => s.Id == id));

        public Task<PlanningSession> UpdateSessionAsync(PlanningSession session, CancellationToken cancellationToken)
            => Task.FromResult(session);

        public Task<bool> DeleteSessionAsync(Guid id, CancellationToken cancellationToken)
        {
            var session = _sessions.FirstOrDefault(s => s.Id == id);
            if (session is null)
            {
                return Task.FromResult(false);
            }

            _sessions.Remove(session);
            return Task.FromResult(true);
        }

        public Task<bool> SetAgreedEstimateAsync(Guid sessionId, Guid taskId, string? estimate, CancellationToken cancellationToken)
        {
            var task = _sessions
                .FirstOrDefault(s => s.Id == sessionId)?.Tasks
                .FirstOrDefault(t => t.Id == taskId);
            if (task is null)
            {
                return Task.FromResult(false);
            }

            task.AgreedEstimate = estimate;
            return Task.FromResult(true);
        }

        public int? LastAdoRevisionSet { get; private set; }

        public Task<bool> SetAdoRevisionAsync(Guid sessionId, Guid taskId, int revision, CancellationToken cancellationToken)
        {
            var task = _sessions
                .FirstOrDefault(s => s.Id == sessionId)?.Tasks
                .FirstOrDefault(t => t.Id == taskId);
            if (task is null)
            {
                return Task.FromResult(false);
            }

            task.AdoRevision = revision;
            LastAdoRevisionSet = revision;
            return Task.FromResult(true);
        }

        public Task<GuestSessionReference?> GetActiveSessionByShareCodeAsync(string shareCode, CancellationToken cancellationToken)
        {
            var session = _sessions.FirstOrDefault(s =>
                s.ShareCode == shareCode && s.Status == SessionStatus.Active);
            return Task.FromResult(session is null
                ? null
                : new GuestSessionReference(session.Id, session.TenantId));
        }

        public Task<bool> ShareCodeExistsAsync(string shareCode, CancellationToken cancellationToken)
            => Task.FromResult(_sessions.Any(s => s.ShareCode == shareCode));
    }

    private sealed class FakeAzureDevOpsWorkItemClient : IAzureDevOpsWorkItemClient
    {
        public AzureDevOpsWriteEstimateRequest? LastWriteRequest { get; private set; }

        public Func<AzureDevOpsWriteEstimateRequest, AzureDevOpsWriteEstimateResult>? OnWrite { get; set; }

        public Task<IReadOnlyCollection<AzureDevOpsWorkItem>> ImportWorkItemsAsync(AzureDevOpsImportRequest request, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyCollection<AzureDevOpsWorkItem>>([]);

        public Task<AzureDevOpsWriteEstimateResult> WriteEstimateAsync(AzureDevOpsWriteEstimateRequest request, CancellationToken cancellationToken)
        {
            LastWriteRequest = request;
            var result = OnWrite?.Invoke(request)
                ?? new AzureDevOpsWriteEstimateResult(request.WorkItemId, request.ExpectedRevision.GetValueOrDefault() + 1);
            return Task.FromResult(result);
        }
    }
}
