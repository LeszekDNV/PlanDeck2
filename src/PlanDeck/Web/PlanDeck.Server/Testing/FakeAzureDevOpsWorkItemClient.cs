using PlanDeck.Application.Abstractions;

namespace PlanDeck.Server.Testing;

/// <summary>
/// Deterministic in-memory <see cref="IAzureDevOpsWorkItemClient"/> used under the test scheme so
/// E2E import flows run without a real Azure DevOps connection or PAT. Mirrors the test-only
/// <c>TestAuthenticationHandler</c> pattern.
/// </summary>
public sealed class FakeAzureDevOpsWorkItemClient : IAzureDevOpsWorkItemClient
{
    private static readonly IReadOnlyList<AzureDevOpsWorkItem> WorkItems =
    [
        new(1001, "Import work items from Azure DevOps", "Active", "User Story", 3, 5, "As a user I can import work items."),
        new(1002, "Fix login redirect loop", "New", "Bug", 1, null, "Steps to reproduce the redirect loop."),
        new(1003, "Add session export", "Active", "Task", 2, 8, null),
    ];

    public Task<IReadOnlyCollection<AzureDevOpsWorkItem>> ImportWorkItemsAsync(
        AzureDevOpsImportRequest request, CancellationToken cancellationToken)
    {
        var limit = request.Limit > 0 ? request.Limit : WorkItems.Count;
        IReadOnlyCollection<AzureDevOpsWorkItem> result = WorkItems.Take(limit).ToList();
        return Task.FromResult(result);
    }

    public Task<AzureDevOpsWriteEstimateResult> WriteEstimateAsync(
        AzureDevOpsWriteEstimateRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(new AzureDevOpsWriteEstimateResult(
            request.WorkItemId, request.ExpectedRevision.GetValueOrDefault() + 1));
}
