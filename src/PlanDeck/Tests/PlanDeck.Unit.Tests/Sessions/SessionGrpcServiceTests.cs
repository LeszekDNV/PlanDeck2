using Grpc.Core;
using PlanDeck.Application.Abstractions;
using PlanDeck.Application.Domain;
using PlanDeck.Application.Services;
using PlanDeck.Core.Shared.Contracts;

namespace PlanDeck.Unit.Tests.Sessions;

[TestFixture]
public sealed class SessionGrpcServiceTests
{
    private FakeSessionRepository _repository = null!;
    private SessionGrpcService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _repository = new FakeSessionRepository();
        _service = new SessionGrpcService(_repository);
    }

    [Test]
    public async Task CreateSession_WithFibonacciScale_ResolvesCanonicalFaces()
    {
        var request = new CreateSessionRequest
        {
            Name = "Sprint 1",
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
    public void AddTask_OnActiveSession_ThrowsFailedPrecondition()
    {
        var session = _repository.Seed(SessionStatus.Active);

        var request = new AddTaskRequest
        {
            SessionId = session.Id,
            Task = new NewSessionTaskDto { Title = "Late task" }
        };

        var ex = Assert.ThrowsAsync<RpcException>(() => _service.AddTaskAsync(request));
        Assert.That(ex!.StatusCode, Is.EqualTo(StatusCode.FailedPrecondition));
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
    }
}
