using PlanDeck.Application.Abstractions;
using PlanDeck.Application.Domain;
using PlanDeck.Application.Planning;

namespace PlanDeck.Unit.Tests.Planning;

[TestFixture]
public sealed class VotingRoundServiceTests
{
    [Test]
    public async Task AuthorizeAndLoadSeed_ReturnsSeed_ForActiveSession()
    {
        var userId = Guid.NewGuid();
        var session = BuildSession(SessionStatus.Active, userId);
        var service = new VotingRoundService(new FakeSessionRepository(session), new FakeSessionMemberRepository());

        var seed = await service.AuthorizeAndLoadSeedAsync(
            session.Id, userId, null, CancellationToken.None);

        Assert.That(seed, Is.Not.Null);
    }

    [Test]
    public async Task AuthorizeAndLoadSeed_ReturnsNull_ForDraftSession()
    {
        var userId = Guid.NewGuid();
        var session = BuildSession(SessionStatus.Draft, userId);
        var service = new VotingRoundService(new FakeSessionRepository(session), new FakeSessionMemberRepository());

        var seed = await service.AuthorizeAndLoadSeedAsync(
            session.Id, userId, null, CancellationToken.None);

        Assert.That(seed, Is.Null);
    }

    [Test]
    public async Task AuthorizeAndLoadSeed_ReturnsNull_ForMissingSession()
    {
        var service = new VotingRoundService(new FakeSessionRepository(null), new FakeSessionMemberRepository());

        var seed = await service.AuthorizeAndLoadSeedAsync(
            Guid.NewGuid(), Guid.NewGuid(), "member@example.com", CancellationToken.None);

        Assert.That(seed, Is.Null);
    }

    [Test]
    public async Task LoadActiveSessionSeed_ReturnsSeed_ForActiveSession()
    {
        var session = BuildSession(SessionStatus.Active);
        var service = new VotingRoundService(new FakeSessionRepository(session), new FakeSessionMemberRepository());

        var seed = await service.LoadActiveSessionSeedAsync(session.Id, CancellationToken.None);

        Assert.That(seed, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(seed!.Tasks.Select(task => task.Title), Is.EqualTo(new[] { "First", "Second" }));
            Assert.That(seed.ScaleValues, Is.EqualTo(new[] { "1", "2", "3" }));
        });
    }

    [Test]
    public async Task LoadActiveSessionSeed_ReturnsNull_ForDraftSession()
    {
        var session = BuildSession(SessionStatus.Draft);
        var service = new VotingRoundService(new FakeSessionRepository(session), new FakeSessionMemberRepository());

        var seed = await service.LoadActiveSessionSeedAsync(session.Id, CancellationToken.None);

        Assert.That(seed, Is.Null);
    }

    [Test]
    public async Task LoadActiveSessionSeed_ReturnsNull_ForMissingSession()
    {
        var service = new VotingRoundService(new FakeSessionRepository(null), new FakeSessionMemberRepository());

        var seed = await service.LoadActiveSessionSeedAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.That(seed, Is.Null);
    }

    private static PlanningSession BuildSession(SessionStatus status, Guid? createdByUserId = null)
    {
        var sessionId = Guid.NewGuid();
        return new PlanningSession
        {
            Id = sessionId,
            Name = "Seeded",
            Status = status,
            ScaleType = VotingScaleType.Custom,
            ScaleValues = ["1", "2", "3"],
            CreatedByUserId = createdByUserId ?? Guid.Empty,
            Tasks =
            [
                new SessionTask { Id = Guid.NewGuid(), SessionId = sessionId, Title = "Second", SortOrder = 1 },
                new SessionTask { Id = Guid.NewGuid(), SessionId = sessionId, Title = "First", SortOrder = 0 }
            ]
        };
    }

    private sealed class FakeSessionRepository(PlanningSession? session) : ISessionRepository
    {
        public Task<PlanningSession?> GetSessionAsync(Guid id, CancellationToken cancellationToken)
            => Task.FromResult(session);

        public Task<PlanningSession> CreateSessionAsync(PlanningSession session, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<PlanningSession>> GetSessionsAsync(CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<PlanningSession> UpdateSessionAsync(PlanningSession session, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<bool> DeleteSessionAsync(Guid id, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<bool> SetAgreedEstimateAsync(Guid sessionId, Guid taskId, string? estimate, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<bool> SetAdoRevisionAsync(Guid sessionId, Guid taskId, int revision, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<GuestSessionReference?> GetActiveSessionByShareCodeAsync(string shareCode, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<bool> ShareCodeExistsAsync(string shareCode, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

    private sealed class FakeSessionMemberRepository : ISessionMemberRepository
    {
        public Task<SessionMember> AssignMemberAsync(Guid sessionId, string email, string? displayName, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<bool> RemoveMemberAsync(Guid sessionId, Guid memberId, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<SessionMember>> GetMembersAsync(Guid sessionId, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<SessionMember>>([]);
    }
}
