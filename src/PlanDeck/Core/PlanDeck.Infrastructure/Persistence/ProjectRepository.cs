using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PlanDeck.Application.Abstractions;
using PlanDeck.Application.Domain;

namespace PlanDeck.Infrastructure.Persistence;

public sealed class ProjectRepository(
    PlanDeckDbContext db,
    ICurrentUserContext currentUser) : IProjectRepository
{
    public async Task<PlanDeckProject> CreateAsync(
        string name,
        string? description,
        string ownerEmail,
        CancellationToken cancellationToken)
    {
        PlanDeckProject? project = null;
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await BeginTransactionAsync(cancellationToken);
            project = new PlanDeckProject
            {
                Name = name,
                Description = description,
                CreatedByUserId = currentUser.UserId
            };
            db.Projects.Add(project);
            db.ProjectMembers.Add(new ProjectMember
            {
                ProjectId = project.Id,
                AppUserId = currentUser.UserId,
                Email = ownerEmail,
                Role = ProjectRole.Owner,
                Status = InvitationStatus.Accepted,
                InvitedByUserId = currentUser.UserId,
                AcceptedAtUtc = DateTimeOffset.UtcNow
            });

            await db.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
        });

        return project
            ?? throw new InvalidOperationException("Project creation did not complete.");
    }

    public async Task<IReadOnlyList<PlanDeckProject>> ListAccessibleAsync(
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || currentUser.IsGuest)
        {
            return [];
        }

        var userId = currentUser.UserId;
        return await db.Projects
            .AsNoTracking()
            .Where(project =>
                db.ProjectMembers.Any(member =>
                    member.ProjectId == project.Id
                    && member.AppUserId == userId
                    && member.Status == InvitationStatus.Accepted)
                || db.ProjectTeams.Any(assignment =>
                    assignment.ProjectId == project.Id
                    && db.TeamMembers.Any(member =>
                        member.TeamId == assignment.TeamId
                        && member.AppUserId == userId
                        && member.Status == InvitationStatus.Accepted)))
            .OrderBy(project => project.Name)
            .ToListAsync(cancellationToken);
    }

    public Task<PlanDeckProject?> GetAsync(
        Guid projectId,
        CancellationToken cancellationToken) =>
        db.Projects.AsNoTracking().SingleOrDefaultAsync(
            project => project.Id == projectId,
            cancellationToken);

    public async Task<IReadOnlyList<ProjectMember>> ListMembersAsync(
        Guid projectId,
        CancellationToken cancellationToken) =>
        await db.ProjectMembers
            .AsNoTracking()
            .Where(member => member.ProjectId == projectId)
            .OrderBy(member => member.Email)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ProjectTeam>> ListTeamsAsync(
        Guid projectId,
        CancellationToken cancellationToken) =>
        await db.ProjectTeams
            .AsNoTracking()
            .Where(assignment => assignment.ProjectId == projectId)
            .OrderBy(assignment => assignment.TeamId)
            .ToListAsync(cancellationToken);

    public async Task<ProjectMember> InviteMemberAsync(
        Guid projectId,
        string email,
        ProjectRole role,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = email.Trim().ToUpperInvariant();
        var appUser = await db.AppUsers.SingleOrDefaultAsync(
            user => user.NormalizedEmail == normalizedEmail && user.IsActive,
            cancellationToken);
        var member = new ProjectMember
        {
            ProjectId = projectId,
            AppUserId = appUser?.Id,
            Email = email,
            Role = role,
            Status = appUser is null ? InvitationStatus.Pending : InvitationStatus.Accepted,
            InvitedByUserId = currentUser.UserId,
            AcceptedAtUtc = appUser is null ? null : DateTimeOffset.UtcNow
        };
        db.ProjectMembers.Add(member);
        await db.SaveChangesAsync(cancellationToken);
        return member;
    }

    public async Task RemoveMemberAsync(
        Guid projectId,
        Guid memberId,
        CancellationToken cancellationToken)
    {
        var member = await LoadMemberAsync(projectId, memberId, cancellationToken);
        if (member.Role == ProjectRole.Owner)
        {
            throw new InvalidOperationException("The project Owner cannot be removed.");
        }

        db.ProjectMembers.Remove(member);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<ProjectMember> ChangeMemberRoleAsync(
        Guid projectId,
        Guid memberId,
        ProjectRole role,
        CancellationToken cancellationToken)
    {
        var member = await LoadMemberAsync(projectId, memberId, cancellationToken);
        if (member.Role == ProjectRole.Owner || role == ProjectRole.Owner)
        {
            throw new InvalidOperationException(
                "Ownership can be changed only through ownership transfer.");
        }

        member.Role = role;
        await db.SaveChangesAsync(cancellationToken);
        return member;
    }

    public async Task<ProjectTeam> AssignTeamAsync(
        Guid projectId,
        Guid teamId,
        CancellationToken cancellationToken)
    {
        var teamExists = await db.Teams.AnyAsync(team => team.Id == teamId, cancellationToken);
        if (!teamExists)
        {
            throw new InvalidOperationException("The team was not found.");
        }

        var assignment = new ProjectTeam
        {
            ProjectId = projectId,
            TeamId = teamId,
            AssignedByUserId = currentUser.UserId
        };
        db.ProjectTeams.Add(assignment);
        await db.SaveChangesAsync(cancellationToken);
        return assignment;
    }

    public async Task UnassignTeamAsync(
        Guid projectId,
        Guid teamId,
        CancellationToken cancellationToken)
    {
        var assignment = await db.ProjectTeams.SingleOrDefaultAsync(
            candidate => candidate.ProjectId == projectId && candidate.TeamId == teamId,
            cancellationToken);
        if (assignment is not null)
        {
            db.ProjectTeams.Remove(assignment);
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task TransferOwnershipAsync(
        Guid projectId,
        Guid newOwnerMemberId,
        CancellationToken cancellationToken)
    {
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await BeginTransactionAsync(cancellationToken);
            var owner = await db.ProjectMembers.SingleAsync(
                member => member.ProjectId == projectId
                    && member.Role == ProjectRole.Owner
                    && member.Status == InvitationStatus.Accepted,
                cancellationToken);
            var newOwner = await LoadMemberAsync(
                projectId,
                newOwnerMemberId,
                cancellationToken);
            if (newOwner.Status != InvitationStatus.Accepted || newOwner.AppUserId is null)
            {
                throw new InvalidOperationException(
                    "Ownership requires an accepted direct member.");
            }

            owner.Role = ProjectRole.Admin;
            await db.SaveChangesAsync(cancellationToken);

            newOwner.Role = ProjectRole.Owner;
            await db.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
        });
    }

    public async Task DeleteAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var project = await db.Projects.SingleOrDefaultAsync(
            candidate => candidate.Id == projectId,
            cancellationToken);
        if (project is null)
        {
            throw new ProjectNotFoundException(projectId);
        }

        db.Projects.Remove(project);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            throw new ProjectPersistenceException(exception);
        }
    }

    private async Task<ProjectMember> LoadMemberAsync(
        Guid projectId,
        Guid memberId,
        CancellationToken cancellationToken) =>
        await db.ProjectMembers.SingleOrDefaultAsync(
            member => member.ProjectId == projectId && member.Id == memberId,
            cancellationToken)
        ?? throw new InvalidOperationException("The project member was not found.");

    private async Task<IDbContextTransaction?> BeginTransactionAsync(
        CancellationToken cancellationToken) =>
        db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;
}

