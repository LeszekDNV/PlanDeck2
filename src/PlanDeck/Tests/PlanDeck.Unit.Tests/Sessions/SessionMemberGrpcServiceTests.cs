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
    private FakeSessionAccessResolver _accessResolver = null!;
    private SessionMemberGrpcService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _repository = new FakeSessionMemberRepository();
        _accessResolver = new FakeSessionAccessResolver();
        _service = new SessionMemberGrpcService(_repository, _accessResolver, new FakeCurrentUserContext());
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

    [TestCase("a@b")]
    [TestCase("##@##")]
    [TestCase("a@b.")]
    [TestCase("foo@bar baz.com")]
    public void AssignMember_WithMalformedEmail_ThrowsInvalidArgument(string email)
    {
        var request = new AssignSessionMemberRequest { SessionId = Guid.NewGuid(), Email = email };

        var ex = Assert.ThrowsAsync<RpcException>(() => _service.AssignMemberAsync(request));
        Assert.That(ex!.StatusCode, Is.EqualTo(StatusCode.InvalidArgument));
    }

    [Test]
    public void AssignMember_WhenSessionMissing_ThrowsNotFound()
    {
        _repository.SessionNotFound = true;
        var sessionId = Guid.NewGuid();
        _accessResolver.SessionRoles[sessionId] = ProjectRole.Admin;
        var request = new AssignSessionMemberRequest { SessionId = sessionId, Email = "a@example.com" };

        var ex = Assert.ThrowsAsync<RpcException>(() => _service.AssignMemberAsync(request));
        Assert.That(ex!.StatusCode, Is.EqualTo(StatusCode.NotFound));
    }

    [Test]
    public void AssignMember_WhenDuplicate_ThrowsAlreadyExists()
    {
        _repository.Duplicate = true;
        var sessionId = Guid.NewGuid();
        _accessResolver.SessionRoles[sessionId] = ProjectRole.Admin;
        var request = new AssignSessionMemberRequest { SessionId = sessionId, Email = "a@example.com" };

        var ex = Assert.ThrowsAsync<RpcException>(() => _service.AssignMemberAsync(request));
        Assert.That(ex!.StatusCode, Is.EqualTo(StatusCode.AlreadyExists));
    }

    [Test]
    public async Task AssignMember_HappyPath_TrimsAndReturnsDto()
    {
        var sessionId = Guid.NewGuid();
        _accessResolver.SessionRoles[sessionId] = ProjectRole.Admin;
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
        _accessResolver.SessionRoles[sessionId] = ProjectRole.Member;
        _repository.Members.Add(new SessionMember { SessionId = sessionId, Email = "a@example.com" });
        _repository.Members.Add(new SessionMember { SessionId = sessionId, Email = "b@example.com" });

        var reply = await _service.ListMembersAsync(new ListSessionMembersRequest { SessionId = sessionId });

        Assert.That(reply.Members.Select(m => m.Email), Is.EqualTo(new[] { "a@example.com", "b@example.com" }));
    }

    [Test]
    public async Task RemoveMember_ReturnsRepositoryResult()
    {
        var sessionId = Guid.NewGuid();
        _accessResolver.SessionRoles[sessionId] = ProjectRole.Admin;
        _repository.RemoveResult = true;

        var reply = await _service.RemoveMemberAsync(new RemoveSessionMemberRequest
        {
            SessionId = sessionId,
            MemberId = Guid.NewGuid()
        });

        Assert.That(reply.Removed, Is.True);
    }

    [Test]
    public void AssignMember_WhenRoleIsMember_ThrowsPermissionDenied()
    {
        var sessionId = Guid.NewGuid();
        _accessResolver.SessionRoles[sessionId] = ProjectRole.Member;

        var ex = Assert.ThrowsAsync<RpcException>(() => _service.AssignMemberAsync(
            new AssignSessionMemberRequest { SessionId = sessionId, Email = "a@example.com" }));
        Assert.That(ex!.StatusCode, Is.EqualTo(StatusCode.PermissionDenied));
    }

    [Test]
    public void RemoveMember_WhenRoleIsMember_ThrowsPermissionDenied()
    {
        var sessionId = Guid.NewGuid();
        _accessResolver.SessionRoles[sessionId] = ProjectRole.Member;

        var ex = Assert.ThrowsAsync<RpcException>(() => _service.RemoveMemberAsync(
            new RemoveSessionMemberRequest { SessionId = sessionId, MemberId = Guid.NewGuid() }));
        Assert.That(ex!.StatusCode, Is.EqualTo(StatusCode.PermissionDenied));
    }

    [Test]
    public void ListMembers_WithoutProjectAccess_ThrowsNotFound()
    {
        var ex = Assert.ThrowsAsync<RpcException>(() => _service.ListMembersAsync(
            new ListSessionMembersRequest { SessionId = Guid.NewGuid() }));
        Assert.That(ex!.StatusCode, Is.EqualTo(StatusCode.NotFound));
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

    private sealed class FakeCurrentUserContext : ICurrentUserContext
    {
        public Guid TenantId { get; } = Guid.NewGuid();
        public Guid UserId { get; } = Guid.NewGuid();
        public bool IsAuthenticated => true;
        public string? DisplayName => null;
        public string? Email => "member@example.com";
    }

    private sealed class FakeSessionAccessResolver : ISessionAccessResolver
    {
        public Dictionary<Guid, ProjectRole> SessionRoles { get; } = [];

        public Task<(Guid ProjectId, ProjectRole Role)?> ResolveProjectAccessAsync(Guid sessionId, CancellationToken cancellationToken)
        {
            if (!SessionRoles.TryGetValue(sessionId, out var role))
            {
                return Task.FromResult< (Guid ProjectId, ProjectRole Role)?>(null);
            }

            return Task.FromResult<(Guid ProjectId, ProjectRole Role)?>((Guid.NewGuid(), role));
        }
    }
}
