using Grpc.Core;
using PlanDeck.Application.Abstractions;
using PlanDeck.Application.Domain;
using PlanDeck.Application.Planning;
using PlanDeck.Core.Shared.Contracts;
using PlanDeck.Core.Shared.Realtime;
using PlanDeck.Core.Shared.Validation;
using ProtoBuf.Grpc;
using System.Text.RegularExpressions;

namespace PlanDeck.Application.Services;

public sealed class ProjectGrpcService(
    IProjectRepository repository,
    IProjectAccessResolver access,
    ICurrentUserContext currentUser,
    ISessionRepository sessions,
    IPlanningRoomService planningRoomService,
    IProjectAzureDevOpsConnectionRepository connections,
    IProjectSecretStore secretStore,
    IAzureDevOpsConnectionValidator connectionValidator,
    TimeProvider timeProvider) : IProjectService
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
        var connection = await connections.GetAsync(
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
            Teams = teams.Select(ToDto).ToList(),
            Connection = connection is null ? null : ToDto(connection)
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
        var ownedSessionIds = (await sessions.GetSessionsAsync(
                request.ProjectId,
                context.CancellationToken))
            .Select(session => session.Id)
            .ToArray();

        var connection = await connections.GetAsync(
            request.ProjectId,
            context.CancellationToken);
        if (connection is not null)
        {
            await SoftDeleteSecretAsync(connection.SecretName, context.CancellationToken);
        }

        await repository.DeleteAsync(request.ProjectId, context.CancellationToken);
        foreach (var sessionId in ownedSessionIds)
        {
            planningRoomService.InvalidateSession(new RoomKey(currentUser.TenantId, sessionId));
        }

        return new EmptyProjectReply();
    }

    public async Task<ProjectConnectionReply> ConfigureConnectionAsync(
        ConfigureProjectConnectionRequest request,
        CallContext context = default)
    {
        await RequireAsync(request.ProjectId, ProjectRole.Owner, context.CancellationToken);
        if (await connections.GetAsync(request.ProjectId, context.CancellationToken) is not null)
        {
            throw FailedPrecondition("The project connection is already configured.");
        }

        var settings = ValidateSettings(
            request.OrganizationUrl,
            request.AzureDevOpsProject,
            request.EstimateField,
            request.DescriptionField,
            request.ReproStepsField,
            request.AcceptanceCriteriaField);
        ValidatePat(request.PersonalAccessToken);
        await ValidateConnectionAsync(
            settings,
            request.PersonalAccessToken,
            context.CancellationToken);

        var secretName = await CreateSecretAsync(
            request.PersonalAccessToken,
            context.CancellationToken);
        var connection = new ProjectAzureDevOpsConnection
        {
            ProjectId = request.ProjectId,
            OrganizationUrl = settings.OrganizationUrl,
            AzureDevOpsProject = settings.Project,
            EstimateField = settings.EstimateField,
            DescriptionField = settings.DescriptionField,
            ReproStepsField = settings.ReproStepsField,
            AcceptanceCriteriaField = settings.AcceptanceCriteriaField,
            SecretName = secretName,
            IsEnabled = true,
            ValidationState = ConnectionValidationState.Valid,
            LastValidatedAtUtc = timeProvider.GetUtcNow()
        };

        try
        {
            await connections.AddAsync(connection, context.CancellationToken);
        }
        catch (ProjectConnectionPersistenceException)
        {
            await SoftDeleteSecretAsync(secretName, context.CancellationToken);
            throw Internal();
        }

        return Reply(connection);
    }

    public async Task<ProjectConnectionReply> UpdateConnectionAsync(
        UpdateProjectConnectionRequest request,
        CallContext context = default)
    {
        await RequireAsync(request.ProjectId, ProjectRole.Owner, context.CancellationToken);
        var connection = await RequireConnectionAsync(
            request.ProjectId,
            context.CancellationToken);
        var settings = ValidateSettings(
            request.OrganizationUrl,
            request.AzureDevOpsProject,
            request.EstimateField,
            request.DescriptionField,
            request.ReproStepsField,
            request.AcceptanceCriteriaField);

        try
        {
            connection.UpdateSettings(
                settings.OrganizationUrl,
                settings.Project,
                settings.EstimateField,
                settings.DescriptionField,
                settings.ReproStepsField,
                settings.AcceptanceCriteriaField,
                timeProvider.GetUtcNow());
        }
        catch (ProjectConnectionTargetLockedException)
        {
            throw FailedPrecondition(
                "The Azure DevOps organization and project can no longer be changed.");
        }

        await ValidateStoredConnectionAsync(
            settings,
            connection.SecretName,
            context.CancellationToken);
        await UpdateConnectionMetadataAsync(connection, context.CancellationToken);
        return Reply(connection);
    }

    public async Task<ProjectConnectionReply> RotateConnectionPatAsync(
        RotateProjectConnectionPatRequest request,
        CallContext context = default)
    {
        await RequireAsync(request.ProjectId, ProjectRole.Owner, context.CancellationToken);
        ValidatePat(request.PersonalAccessToken);
        var connection = await RequireConnectionAsync(
            request.ProjectId,
            context.CancellationToken);
        var settings = Settings(connection);
        await ValidateConnectionAsync(
            settings,
            request.PersonalAccessToken,
            context.CancellationToken);
        await RotateSecretAsync(
            connection.SecretName,
            request.PersonalAccessToken,
            context.CancellationToken);
        connection.MarkValidated(timeProvider.GetUtcNow());
        await UpdateConnectionMetadataAsync(connection, context.CancellationToken);
        return Reply(connection);
    }

    public async Task<ProjectConnectionReply> SetConnectionEnabledAsync(
        SetProjectConnectionEnabledRequest request,
        CallContext context = default)
    {
        await RequireAsync(request.ProjectId, ProjectRole.Owner, context.CancellationToken);
        var connection = await RequireConnectionAsync(
            request.ProjectId,
            context.CancellationToken);
        connection.IsEnabled = request.IsEnabled;
        await UpdateConnectionMetadataAsync(connection, context.CancellationToken);
        return Reply(connection);
    }

    public async Task<EmptyProjectReply> RemoveConnectionAsync(
        RemoveProjectConnectionRequest request,
        CallContext context = default)
    {
        await RequireAsync(request.ProjectId, ProjectRole.Owner, context.CancellationToken);
        var connection = await RequireConnectionAsync(
            request.ProjectId,
            context.CancellationToken);
        await SoftDeleteSecretAsync(connection.SecretName, context.CancellationToken);
        try
        {
            await connections.DeleteAsync(connection, context.CancellationToken);
        }
        catch (ProjectConnectionPersistenceException)
        {
            throw Internal();
        }

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

    private async Task ValidateStoredConnectionAsync(
        ConnectionSettings settings,
        string secretName,
        CancellationToken cancellationToken)
    {
        var pat = await GetSecretAsync(secretName, cancellationToken);
        try
        {
            await connectionValidator.ValidateAsync(
                new AzureDevOpsConnectionValidationRequest(
                    settings.OrganizationUrl,
                    settings.Project,
                    pat),
                cancellationToken);
        }
        catch (AzureDevOpsConnectionValidationException exception)
            when (exception.Failure
                == AzureDevOpsConnectionValidationFailure.InvalidCredentials)
        {
            secretStore.Invalidate(secretName);
            var latestPat = await GetSecretAsync(secretName, cancellationToken);
            await ValidateConnectionAsync(settings, latestPat, cancellationToken);
        }
        catch (AzureDevOpsConnectionValidationException exception)
            when (exception.Failure == AzureDevOpsConnectionValidationFailure.Unavailable)
        {
            throw new RpcException(new Status(
                StatusCode.Unavailable,
                "Azure DevOps is temporarily unavailable."));
        }
        catch (AzureDevOpsConnectionValidationException)
        {
            throw InvalidArgument(
                "The Azure DevOps credentials or target could not be validated.");
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

    private static ProjectConnectionDto ToDto(ProjectAzureDevOpsConnection connection) => new()
    {
        IsEnabled = connection.IsEnabled,
        ValidationState =
            (ProjectConnectionValidationStateDto)(int)connection.ValidationState,
        LastValidatedAtUtc = connection.LastValidatedAtUtc?.UtcDateTime
    };

    private static ProjectConnectionReply Reply(ProjectAzureDevOpsConnection connection) =>
        new() { Connection = ToDto(connection) };

    private async Task<ProjectAzureDevOpsConnection> RequireConnectionAsync(
        Guid projectId,
        CancellationToken cancellationToken) =>
        await connections.GetAsync(projectId, cancellationToken)
        ?? throw FailedPrecondition("The project connection is not configured.");

    private async Task UpdateConnectionMetadataAsync(
        ProjectAzureDevOpsConnection connection,
        CancellationToken cancellationToken)
    {
        try
        {
            await connections.UpdateAsync(connection, cancellationToken);
        }
        catch (ProjectConnectionPersistenceException)
        {
            throw Internal();
        }
    }

    private async Task ValidateConnectionAsync(
        ConnectionSettings settings,
        string pat,
        CancellationToken cancellationToken)
    {
        try
        {
            await connectionValidator.ValidateAsync(
                new AzureDevOpsConnectionValidationRequest(
                    settings.OrganizationUrl,
                    settings.Project,
                    pat),
                cancellationToken);
        }
        catch (AzureDevOpsConnectionValidationException exception)
            when (exception.Failure == AzureDevOpsConnectionValidationFailure.Unavailable)
        {
            throw new RpcException(new Status(
                StatusCode.Unavailable,
                "Azure DevOps is temporarily unavailable."));
        }
        catch (AzureDevOpsConnectionValidationException)
        {
            throw InvalidArgument(
                "The Azure DevOps credentials or target could not be validated.");
        }
    }

    private async Task<string> CreateSecretAsync(
        string pat,
        CancellationToken cancellationToken)
    {
        try
        {
            return await secretStore.CreateAsync(pat, cancellationToken);
        }
        catch (ProjectSecretStoreException exception)
        {
            throw MapSecretFailure(exception);
        }
    }

    private async Task<string> GetSecretAsync(
        string secretName,
        CancellationToken cancellationToken)
    {
        try
        {
            return await secretStore.GetLatestAsync(secretName, cancellationToken);
        }
        catch (ProjectSecretStoreException exception)
        {
            throw MapSecretFailure(exception);
        }
    }

    private async Task RotateSecretAsync(
        string secretName,
        string pat,
        CancellationToken cancellationToken)
    {
        try
        {
            await secretStore.RotateAsync(secretName, pat, cancellationToken);
            secretStore.Invalidate(secretName);
        }
        catch (ProjectSecretStoreException exception)
        {
            throw MapSecretFailure(exception);
        }
    }

    private async Task SoftDeleteSecretAsync(
        string secretName,
        CancellationToken cancellationToken)
    {
        try
        {
            await secretStore.SoftDeleteAsync(secretName, cancellationToken);
            secretStore.Invalidate(secretName);
        }
        catch (ProjectSecretStoreException exception)
        {
            throw MapSecretFailure(exception);
        }
    }

    private static ConnectionSettings ValidateSettings(
        string organizationUrl,
        string project,
        string estimateField,
        string descriptionField,
        string reproStepsField,
        string acceptanceCriteriaField)
    {
        var normalizedUrl = NormalizeOrganizationUrl(organizationUrl);
        var normalizedProject = project?.Trim() ?? string.Empty;
        if (normalizedProject.Length is 0 or > 256
            || normalizedProject.Any(character =>
                char.IsControl(character) || "/\\?#".Contains(character)))
        {
            throw InvalidArgument("A valid Azure DevOps project name is required.");
        }

        return new ConnectionSettings(
            normalizedUrl,
            normalizedProject,
            ValidateField(estimateField),
            ValidateField(descriptionField),
            ValidateField(reproStepsField),
            ValidateField(acceptanceCriteriaField));
    }

    private static string NormalizeOrganizationUrl(string organizationUrl)
    {
        if (!Uri.TryCreate(organizationUrl?.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !uri.IsDefaultPort
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw InvalidArgument("A valid HTTPS Azure DevOps organization URL is required.");
        }

        var segments = uri.AbsolutePath.Split(
            '/',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var validDevAzure = string.Equals(
                uri.Host,
                "dev.azure.com",
                StringComparison.OrdinalIgnoreCase)
            && segments.Length == 1;
        var validVisualStudio = uri.Host.EndsWith(
                ".visualstudio.com",
                StringComparison.OrdinalIgnoreCase)
            && !string.Equals(uri.Host, "visualstudio.com", StringComparison.OrdinalIgnoreCase)
            && segments.Length == 0;
        if (!validDevAzure && !validVisualStudio)
        {
            throw InvalidArgument("A valid Azure DevOps organization host is required.");
        }

        return uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
    }

    private static string ValidateField(string field)
    {
        var normalized = field?.Trim() ?? string.Empty;
        if (normalized.Length is 0 or > 256
            || !Regex.IsMatch(
                normalized,
                "^[A-Za-z][A-Za-z0-9]*(\\.[A-Za-z][A-Za-z0-9]*)+$",
                RegexOptions.CultureInvariant))
        {
            throw InvalidArgument("Azure DevOps field mappings must be valid reference names.");
        }

        return normalized;
    }

    private static void ValidatePat(string pat)
    {
        if (string.IsNullOrWhiteSpace(pat)
            || pat.Length > 512
            || pat.Any(char.IsControl))
        {
            throw InvalidArgument("A valid Azure DevOps personal access token is required.");
        }
    }

    private static ConnectionSettings Settings(ProjectAzureDevOpsConnection connection) => new(
        connection.OrganizationUrl,
        connection.AzureDevOpsProject,
        connection.EstimateField,
        connection.DescriptionField,
        connection.ReproStepsField,
        connection.AcceptanceCriteriaField);

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }

    private static RpcException InvalidArgument(string detail) =>
        new(new Status(StatusCode.InvalidArgument, detail));

    private static RpcException NotFound() =>
        new(new Status(StatusCode.NotFound, "Project was not found."));

    private static RpcException FailedPrecondition(string detail) =>
        new(new Status(StatusCode.FailedPrecondition, detail));

    private static RpcException Internal() =>
        new(new Status(StatusCode.Internal, "The project connection operation could not be completed."));

    private static RpcException MapSecretFailure(ProjectSecretStoreException exception) =>
        exception switch
        {
            ProjectSecretMissingException => FailedPrecondition(
                "The project connection secret is unavailable."),
            ProjectSecretForbiddenException => new RpcException(new Status(
                StatusCode.Unavailable,
                "The project secret store cannot be accessed.")),
            _ => new RpcException(new Status(
                StatusCode.Unavailable,
                "The project secret store is temporarily unavailable."))
        };

    private sealed record ConnectionSettings(
        string OrganizationUrl,
        string Project,
        string EstimateField,
        string DescriptionField,
        string ReproStepsField,
        string AcceptanceCriteriaField);
}
