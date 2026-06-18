namespace PlanDeck.Application.Domain;

public sealed class SessionTask : TenantEntity
{
    public Guid SessionId { get; set; }

    public required string Title { get; set; }

    public TaskSource Source { get; set; } = TaskSource.AdHoc;

    public int SortOrder { get; set; }

    public int? AdoWorkItemId { get; set; }

    public int? AdoRevision { get; set; }

    public string? WorkItemType { get; set; }

    public string? State { get; set; }
}
