using Grpc.Core;
using PlanDeck.Application.Abstractions;
using PlanDeck.Application.Domain;
using PlanDeck.Core.Shared.Contracts;
using PlanDeck.Core.Shared.Validation;
using ProtoBuf.Grpc;

namespace PlanDeck.Application.Services;

public sealed class ProjectGrpcService(
    IProjectRepository repository,
    IProjectAccessResolver access,
    ICurrentUserContext currentUser) : IProjectService
{
    public async Task<CreateProjectReply> CreateProjectAsync(
        CreateProjectRequest request,
        CallContext context = default)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        var email = currentUser.Email?.Trim() ?? string.Empty;
        if (name.Length == 0 || !EmailValidator.IsValid(email))
        {
            throw InvalidArgument("Project name and a valid member email are required.");
        }

        var project = await repository.CreateAsync(
            name,
            NormalizeOptional(request.Description),
            email,
            context.CancellationToken);
        return new CreateProjectReply
        {
            Project = ToDto(
                project,
                ProjectRole.Owner,
                ProjectMembershipSourceDto.Direct)
        };
    }

    public async Task<ListProjectsReply> ListProjectsAsync(
        ListProjectsRequest request,
        CallContext context = default)
    {
        var projects = await repository.ListAccessibleAsync(context.CancellationToken);
        var result = new List<ProjectDto>(projects.Count);
        foreach (var project in projects)
        {
            var role = await access.GetEffectiveRoleAsync(
                project.Id,
                context.CancellationToken)
                ?? throw new InvalidOperationException("Accessible project has no effective role.");
            var direct = (await repository.ListMembersAsync(
                project.Id,
                context.CancellationToken))
                .Any(member => member.AppUserId == currentUser.UserId
                    && member.Status == InvitationStatus.Accepted);
            result.Add(ToDto(
                project,
                role,
                direct
                    ? ProjectMembershipSourceDto.Direct
                    : ProjectMembershipSourceDto.Team));
        }

        return new ListProjectsReply { Projects = result };
    }

    public async Task<GetProjectReply> GetProjectAsync(
        GetProjectRequest request,
        CallContext context = default)
    {
        var role = await RequireAsync(
            request.ProjectId,
            ProjectRole.Member,
            context.CancellationToken);
        var project = await repository.GetAsync(
            request.ProjectId,
            context.CancellationToken)
            ?? throw NotFound();
        var members = await repository.ListMembersAsync(
            request.ProjectId,
            context.CancellationToken);
        var direct = members.Any(member => member.AppUserId == currentUser.UserId
            && member.Status == InvitationStatus.Accepted);
        var teams = await repository.ListTeamsAsync(
            request.ProjectId,
            context.CancellationToken);

        return new GetProjectReply
        {
            Project = ToDto(
                project,
                role,
                direct
                    ? ProjectMembershipSourceDto.Direct
                    : ProjectMembershipSourceDto.Team),
            Members = members.Select(ToDto).ToList(),
            Teams = teams.Select(ToDto).ToList()
        };
    }

    public async Task<ProjectMemberReply> InviteMemberAsync(
        InviteProjectMemberRequest request,
        CallContext context = default)
    {
        await RequireAsync(request.ProjectId, ProjectRole.Admin, context.CancellationToken);
        var role = ToRole(request.Role);
        if (role == ProjectRole.Owner || !EmailValidator.IsValid(request.Email))
        {
            throw InvalidArgument("Only Member or Admin invitations with a valid email are allowed.");
        }

        var member = await repository.InviteMemberAsync(
            request.ProjectId,
            request.Email.Trim(),
            role,
            context.CancellationToken);
        return new ProjectMemberReply { Member = ToDto(member) };
    }

    public async Task<ProjectMemberReply> ChangeMemberRoleAsync(
        ChangeProjectMemberRoleRequest request,
        CallContext context = default)
    {
        await RequireAsync(request.ProjectId, ProjectRole.Admin, context.CancellationToken);
        var member = await repository.ChangeMemberRoleAsync(
            request.ProjectId,
            request.MemberId,
            ToRole(request.Role),
            context.CancellationToken);
        return new ProjectMemberReply { Member = ToDto(member) };
    }

    public async Task<EmptyProjectReply> RemoveMemberAsync(
        RemoveProjectMemberRequest request,
        CallContext context = default)
    {
        await RequireAsync(request.ProjectId, ProjectRole.Admin, context.CancellationToken);
        await repository.RemoveMemberAsync(
            request.ProjectId,
            request.MemberId,
            context.CancellationToken);
        return new EmptyProjectReply();
    }

    public async Task<ProjectTeamReply> AssignTeamAsync(
        AssignProjectTeamRequest request,
        CallContext context = default)
    {
        await RequireAsync(request.ProjectId, ProjectRole.Admin, context.CancellationToken);
        var team = await repository.AssignTeamAsync(
            request.ProjectId,
            request.TeamId,
            context.CancellationToken);
        return new ProjectTeamReply { Team = ToDto(team) };
    }

    public async Task<EmptyProjectReply> UnassignTeamAsync(
        UnassignProjectTeamRequest request,
        CallContext context = default)
    {
        await RequireAsync(request.ProjectId, ProjectRole.Admin, context.CancellationToken);
        await repository.UnassignTeamAsync(
            request.ProjectId,
            request.TeamId,
            context.CancellationToken);
        return new EmptyProjectReply();
    }

    public async Task<EmptyProjectReply> TransferOwnershipAsync(
        TransferProjectOwnershipRequest request,
        CallContext context = default)
    {
        await RequireAsync(request.ProjectId, ProjectRole.Owner, context.CancellationToken);
        await repository.TransferOwnershipAsync(
            request.ProjectId,
            request.NewOwnerMemberId,
            context.CancellationToken);
        return new EmptyProjectReply();
    }

    public async Task<EmptyProjectReply> DeleteProjectAsync(
        DeleteProjectRequest request,
        CallContext context = default)
    {
        await RequireAsync(request.ProjectId, ProjectRole.Owner, context.CancellationToken);
        await repository.DeleteAsync(request.ProjectId, context.CancellationToken);
        return new EmptyProjectReply();
    }

    private async Task<ProjectRole> RequireAsync(
        Guid projectId,
        ProjectRole role,
        CancellationToken cancellationToken)
    {
        if (projectId == Guid.Empty)
        {
            throw InvalidArgument("ProjectId is required.");
        }

        try
        {
            return await access.RequireRoleAsync(projectId, role, cancellationToken);
        }
        catch (ProjectNotFoundException)
        {
            throw NotFound();
        }
        catch (ProjectPermissionDeniedException)
        {
            throw new RpcException(new Status(
                StatusCode.PermissionDenied,
                "The requested project action is not permitted."));
        }
    }

    private static ProjectRole ToRole(ProjectRoleDto role) => role switch
    {
        ProjectRoleDto.Member => ProjectRole.Member,
        ProjectRoleDto.Admin => ProjectRole.Admin,
        ProjectRoleDto.Owner => ProjectRole.Owner,
        _ => throw InvalidArgument("Unknown project role.")
    };

    private static ProjectDto ToDto(
        PlanDeckProject project,
        ProjectRole role,
        ProjectMembershipSourceDto source) => new()
    {
        Id = project.Id,
        Name = project.Name,
        Description = project.Description,
        EffectiveRole = (ProjectRoleDto)(int)role,
        MembershipSource = source
    };

    private static ProjectMemberDto ToDto(ProjectMember member) => new()
    {
        Id = member.Id,
        Email = member.Email,
        Role = (ProjectRoleDto)(int)member.Role,
        Status = (InvitationStatusDto)(int)member.Status
    };

    private static ProjectTeamDto ToDto(ProjectTeam team) => new()
    {
        Id = team.Id,
        TeamId = team.TeamId
    };

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }

    private static RpcException InvalidArgument(string detail) =>
        new(new Status(StatusCode.InvalidArgument, detail));

    private static RpcException NotFound() =>
        new(new Status(StatusCode.NotFound, "Project was not found."));
}
