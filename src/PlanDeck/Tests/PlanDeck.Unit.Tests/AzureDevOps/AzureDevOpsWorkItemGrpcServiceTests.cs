using PlanDeck.Application.Abstractions;
using PlanDeck.Application.Domain;
using PlanDeck.Application.Services;
using PlanDeck.Core.Shared.Contracts;

namespace PlanDeck.Unit.Tests.AzureDevOps;

[TestFixture]
public sealed class AzureDevOpsWorkItemGrpcServiceTests
{
    private static readonly Guid ProjectId = Guid.Parse("11111111-0000-0000-0000-000000000001");

    private FakeAzureDevOpsWorkItemClient _client = null!;
    private AzureDevOpsWorkItemGrpcService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _client = new FakeAzureDevOpsWorkItemClient();
        _service = new AzureDevOpsWorkItemGrpcService(
            _client,
            new FakeCurrentUserContext(),
            new AlwaysGrantedAccessResolver(),
            new StubAdoConnectionContextResolver());
    }

    [Test]
    public async Task ImportWorkItemsAsync_BuildsWhereClauseFromFiltersAndForwardsLimit()
    {
        var request = new ImportWorkItemsRequest
        {
            ProjectId = ProjectId,
            WorkItemTypes = ["Bug"],
            States = ["Active"],
            Limit = 25
        };

        await _service.ImportWorkItemsAsync(request);

        Assert.That(_client.LastImportRequest, Is.Not.Null);
        Assert.That(_client.LastImportRequest!.WiqlWhereClause, Is.EqualTo(
            "[System.WorkItemType] IN ('Bug') AND [System.State] IN ('Active')"));
        Assert.That(_client.LastImportRequest!.Limit, Is.EqualTo(25));
    }

    [Test]
    public async Task ImportWorkItemsAsync_WithNoFilters_ForwardsNullWhereClause()
    {
        await _service.ImportWorkItemsAsync(new ImportWorkItemsRequest { ProjectId = ProjectId, Limit = 10 });

        Assert.That(_client.LastImportRequest, Is.Not.Null);
        Assert.That(_client.LastImportRequest!.WiqlWhereClause, Is.Null);
        Assert.That(_client.LastImportRequest!.Limit, Is.EqualTo(10));
    }

    [Test]
    public async Task ImportWorkItemsAsync_MapsEveryFieldOntoDto()
    {
        _client.ItemsToReturn =
        [
            new AzureDevOpsWorkItem(42, "Title", "Active", "Bug", 7, 3.5, "Description")
        ];

        var reply = await _service.ImportWorkItemsAsync(new ImportWorkItemsRequest { ProjectId = ProjectId });

        Assert.That(reply.WorkItems, Has.Count.EqualTo(1));
        var dto = reply.WorkItems[0];
        Assert.Multiple(() =>
        {
            Assert.That(dto.Id, Is.EqualTo(42));
            Assert.That(dto.Title, Is.EqualTo("Title"));
            Assert.That(dto.State, Is.EqualTo("Active"));
            Assert.That(dto.WorkItemType, Is.EqualTo("Bug"));
            Assert.That(dto.Revision, Is.EqualTo(7));
            Assert.That(dto.Estimate, Is.EqualTo(3.5));
            Assert.That(dto.Description, Is.EqualTo("Description"));
        });
    }

    private sealed class FakeAzureDevOpsWorkItemClient : IAzureDevOpsWorkItemClient
    {
        public AzureDevOpsImportRequest? LastImportRequest { get; private set; }

        public IReadOnlyCollection<AzureDevOpsWorkItem> ItemsToReturn { get; set; } = [];

        public Task<IReadOnlyCollection<AzureDevOpsWorkItem>> ImportWorkItemsAsync(
            AdoConnectionContext connection, AzureDevOpsImportRequest request, CancellationToken cancellationToken)
        {
            LastImportRequest = request;
            return Task.FromResult(ItemsToReturn);
        }

        public Task<AzureDevOpsWorkItem?> GetWorkItemByIdAsync(
            AdoConnectionContext connection, int workItemId, CancellationToken cancellationToken)
            => Task.FromResult<AzureDevOpsWorkItem?>(null);

        public Task<AzureDevOpsWriteEstimateResult> WriteEstimateAsync(
            AdoConnectionContext connection, AzureDevOpsWriteEstimateRequest request, CancellationToken cancellationToken)
            => Task.FromResult(new AzureDevOpsWriteEstimateResult(request.WorkItemId, 1));
    }

    private sealed class FakeCurrentUserContext : ICurrentUserContext
    {
        public Guid TenantId { get; } = Guid.NewGuid();
        public Guid UserId { get; } = Guid.NewGuid();
        public bool IsAuthenticated => true;
        public string? DisplayName => null;
        public string? Email => "member@example.com";
    }

    private sealed class AlwaysGrantedAccessResolver : IProjectAccessResolver
    {
        public Task<ProjectRole?> GetEffectiveRoleAsync(Guid projectId, CancellationToken cancellationToken)
            => Task.FromResult<ProjectRole?>(ProjectRole.Member);

        public Task<ProjectRole> RequireRoleAsync(Guid projectId, ProjectRole minimumRole, CancellationToken cancellationToken)
            => Task.FromResult(ProjectRole.Member);
    }

    private sealed class StubAdoConnectionContextResolver : IAdoConnectionContextResolver
    {
        public Task<AdoConnectionContext> ResolveAsync(Guid projectId, CancellationToken cancellationToken)
            => Task.FromResult(new AdoConnectionContext(
                "https://dev.azure.com/test", "TestProject", "fake-pat",
                "Story Points", "Description", "Repro Steps", "Acceptance Criteria"));
    }
}
