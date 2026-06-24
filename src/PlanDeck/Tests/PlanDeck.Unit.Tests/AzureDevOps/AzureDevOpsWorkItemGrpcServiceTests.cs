using PlanDeck.Application.Abstractions;
using PlanDeck.Application.Services;
using PlanDeck.Core.Shared.Contracts;

namespace PlanDeck.Unit.Tests.AzureDevOps;

[TestFixture]
public sealed class AzureDevOpsWorkItemGrpcServiceTests
{
    private FakeAzureDevOpsWorkItemClient _client = null!;
    private AzureDevOpsWorkItemGrpcService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _client = new FakeAzureDevOpsWorkItemClient();
        _service = new AzureDevOpsWorkItemGrpcService(_client);
    }

    [Test]
    public async Task ImportWorkItemsAsync_ForwardsWhereClauseAndLimit()
    {
        var request = new ImportWorkItemsRequest
        {
            WiqlWhereClause = "[System.State] IN ('Active')",
            Limit = 25
        };

        await _service.ImportWorkItemsAsync(request);

        Assert.That(_client.LastImportRequest, Is.Not.Null);
        Assert.That(_client.LastImportRequest!.WiqlWhereClause, Is.EqualTo("[System.State] IN ('Active')"));
        Assert.That(_client.LastImportRequest!.Limit, Is.EqualTo(25));
    }

    [Test]
    public async Task ImportWorkItemsAsync_MapsEveryFieldOntoDto()
    {
        _client.ItemsToReturn =
        [
            new AzureDevOpsWorkItem(42, "Title", "Active", "Bug", 7, 3.5, "Description")
        ];

        var reply = await _service.ImportWorkItemsAsync(new ImportWorkItemsRequest());

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

    [Test]
    public async Task WriteEstimateAsync_ForwardsRequestAndMapsResult()
    {
        var request = new WriteEstimateRequest
        {
            WorkItemId = 99,
            ExpectedRevision = 4,
            Estimate = 8
        };

        var reply = await _service.WriteEstimateAsync(request);

        Assert.That(_client.LastWriteRequest, Is.Not.Null);
        Assert.That(_client.LastWriteRequest!.WorkItemId, Is.EqualTo(99));
        Assert.That(_client.LastWriteRequest!.ExpectedRevision, Is.EqualTo(4));
        Assert.That(_client.LastWriteRequest!.Estimate, Is.EqualTo(8));
        Assert.That(reply.WorkItemId, Is.EqualTo(99));
        Assert.That(reply.Revision, Is.EqualTo(5));
    }

    private sealed class FakeAzureDevOpsWorkItemClient : IAzureDevOpsWorkItemClient
    {
        public AzureDevOpsImportRequest? LastImportRequest { get; private set; }

        public AzureDevOpsWriteEstimateRequest? LastWriteRequest { get; private set; }

        public IReadOnlyCollection<AzureDevOpsWorkItem> ItemsToReturn { get; set; } = [];

        public Task<IReadOnlyCollection<AzureDevOpsWorkItem>> ImportWorkItemsAsync(
            AzureDevOpsImportRequest request, CancellationToken cancellationToken)
        {
            LastImportRequest = request;
            return Task.FromResult(ItemsToReturn);
        }

        public Task<AzureDevOpsWriteEstimateResult> WriteEstimateAsync(
            AzureDevOpsWriteEstimateRequest request, CancellationToken cancellationToken)
        {
            LastWriteRequest = request;
            return Task.FromResult(new AzureDevOpsWriteEstimateResult(
                request.WorkItemId, request.ExpectedRevision.GetValueOrDefault() + 1));
        }
    }
}
