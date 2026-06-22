using Grpc.Core;
using PlanDeck.Application.Abstractions;
using PlanDeck.Application.Domain;
using PlanDeck.Application.Services;
using PlanDeck.Core.Shared.Contracts;

namespace PlanDeck.Unit.Tests.Sessions;

[TestFixture]
public sealed class SessionMemberGrpcServiceTests
{
    private FakeSessionMemberRepository _repository = null!;
    private SessionMemberGrpcService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _repository = new FakeSessionMemberRepository();
        _service = new SessionMemberGrpcService(_repository);
    }

    [Test]
    public void AssignMember_WithEmptySessionId_ThrowsInvalidArgument()
    {
        var request = new AssignSessionMemberRequest { SessionId = Guid.Empty, Email = "a@example.com" };

        var ex = Assert.ThrowsAsync<RpcException>(() => _service.AssignMemberAsync(request));
        Assert.That(ex!.StatusCode, Is.EqualTo(StatusCode.InvalidArgument));
    }

    [Test]
    public void AssignMember_WithMissingEmail_ThrowsInvalidArgument()
    {
        var request = new AssignSessionMemberRequest { SessionId = Guid.NewGuid(), Email = "  " };

        var ex = Assert.ThrowsAsync<RpcException>(() => _service.AssignMemberAsync(request));
        Assert.That(ex!.StatusCode, Is.EqualTo(StatusCode.InvalidArgument));
    }

    [Test]
    public void AssignMember_WithEmailWithoutAt_ThrowsInvalidArgument()
    {
        var request = new AssignSessionMemberRequest { SessionId = Guid.NewGuid(), Email = "not-an-email" };

        var ex = Assert.ThrowsAsync<RpcException>(() => _service.AssignMemberAsync(request));
        Assert.That(ex!.StatusCode, Is.EqualTo(StatusCode.InvalidArgument));
    }

    [Test]
    public void AssignMember_WhenSessionMissing_ThrowsNotFound()
    {
        _repository.SessionNotFound = true;
        var request = new AssignSessionMemberRequest { SessionId = Guid.NewGuid(), Email = "a@example.com" };

        var ex = Assert.ThrowsAsync<RpcException>(() => _service.AssignMemberAsync(request));
        Assert.That(ex!.StatusCode, Is.EqualTo(StatusCode.NotFound));
    }

    [Test]
    public void AssignMember_WhenDuplicate_ThrowsAlreadyExists()
    {
        _repository.Duplicate = true;
        var request = new AssignSessionMemberRequest { SessionId = Guid.NewGuid(), Email = "a@example.com" };

        var ex = Assert.ThrowsAsync<RpcException>(() => _service.AssignMemberAsync(request));
        Assert.That(ex!.StatusCode, Is.EqualTo(StatusCode.AlreadyExists));
    }

    [Test]
    public async Task AssignMember_HappyPath_TrimsAndReturnsDto()
    {
        var sessionId = Guid.NewGuid();
        var request = new AssignSessionMemberRequest { SessionId = sessionId, Email = " a@example.com ", DisplayName = "  Ada  " };

        var reply = await _service.AssignMemberAsync(request);

        Assert.Multiple(() =>
        {
            Assert.That(reply.Member.SessionId, Is.EqualTo(sessionId));
            Assert.That(reply.Member.Email, Is.EqualTo("a@example.com"));
            Assert.That(reply.Member.DisplayName, Is.EqualTo("Ada"));
        });
    }

    [Test]
    public async Task ListMembers_MapsAllMembers()
    {
        var sessionId = Guid.NewGuid();
        _repository.Members.Add(new SessionMember { SessionId = sessionId, Email = "a@example.com" });
        _repository.Members.Add(new SessionMember { SessionId = sessionId, Email = "b@example.com" });

        var reply = await _service.ListMembersAsync(new ListSessionMembersRequest { SessionId = sessionId });

        Assert.That(reply.Members.Select(m => m.Email), Is.EqualTo(new[] { "a@example.com", "b@example.com" }));
    }

    [Test]
    public async Task RemoveMember_ReturnsRepositoryResult()
    {
        _repository.RemoveResult = true;

        var reply = await _service.RemoveMemberAsync(new RemoveSessionMemberRequest
        {
            SessionId = Guid.NewGuid(),
            MemberId = Guid.NewGuid()
        });

        Assert.That(reply.Removed, Is.True);
    }

    private sealed class FakeSessionMemberRepository : ISessionMemberRepository
    {
        public List<SessionMember> Members { get; } = [];

        public bool SessionNotFound { get; set; }

        public bool Duplicate { get; set; }

        public bool RemoveResult { get; set; }

        public Task<SessionMember> AssignMemberAsync(Guid sessionId, string email, string? displayName, CancellationToken cancellationToken)
        {
            if (SessionNotFound)
            {
                throw new SessionNotFoundException(sessionId);
            }

            if (Duplicate)
            {
                throw new DuplicateSessionMemberException(sessionId, email);
            }

            var member = new SessionMember
            {
                SessionId = sessionId,
                Email = email,
                DisplayName = displayName
            };
            Members.Add(member);
            return Task.FromResult(member);
        }

        public Task<bool> RemoveMemberAsync(Guid sessionId, Guid memberId, CancellationToken cancellationToken)
            => Task.FromResult(RemoveResult);

        public Task<IReadOnlyList<SessionMember>> GetMembersAsync(Guid sessionId, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<SessionMember>>(Members);
    }
}
