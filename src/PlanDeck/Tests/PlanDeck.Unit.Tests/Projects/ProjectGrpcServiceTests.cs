using Grpc.Core;
using PlanDeck.Application.Abstractions;
using PlanDeck.Application.Domain;
using PlanDeck.Application.Services;
using PlanDeck.Core.Shared.Contracts;

namespace PlanDeck.Unit.Tests.Projects;

[TestFixture]
public sealed class ProjectGrpcServiceTests
{
    private static readonly Guid ProjectId = Guid.NewGuid();
    private RecordingProjectAccessResolver _access = null!;
    private FakeProjectRepository _repository = null!;
    private ProjectGrpcService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _access = new RecordingProjectAccessResolver();
        _repository = new FakeProjectRepository();
        _service = new ProjectGrpcService(
            _repository,
            _access,
            new FakeCurrentUserContext());
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
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

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

    private sealed class FakeCurrentUserContext : ICurrentUserContext
    {
        public Guid TenantId { get; } = Guid.NewGuid();

        public Guid UserId { get; } = Guid.NewGuid();

        public bool IsAuthenticated => true;

        public string? DisplayName => "Owner";

        public string? Email => "owner@example.com";
    }
}
