namespace PlanDeck.Application.Abstractions;

public sealed class DuplicateProjectMemberException(Guid projectId, string email)
    : Exception($"A member with email '{email}' already exists in project '{projectId}'.")
{
    public Guid ProjectId { get; } = projectId;

    public string Email { get; } = email;
}
