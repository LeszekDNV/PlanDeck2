using System.Runtime.Serialization;
using ProtoBuf.Grpc;
using ProtoBuf.Grpc.Configuration;

namespace PlanDeck.Core.Shared.Contracts;

[Service]
public interface IAzureDevOpsWorkItemService
{
    [Operation]
    Task<ImportWorkItemsReply> ImportWorkItemsAsync(ImportWorkItemsRequest request, CallContext context = default);

    [Operation]
    Task<WriteEstimateReply> WriteEstimateAsync(WriteEstimateRequest request, CallContext context = default);
}

[DataContract]
public sealed class ImportWorkItemsRequest
{
    [DataMember(Order = 1)]
    public List<string> WorkItemTypes { get; set; } = [];

    [DataMember(Order = 2)]
    public List<string> States { get; set; } = [];

    [DataMember(Order = 3)]
    public int Limit { get; set; } = 100;
}

[DataContract]
public sealed class ImportWorkItemsReply
{
    [DataMember(Order = 1)]
    public List<AzureDevOpsWorkItemDto> WorkItems { get; set; } = [];
}

[DataContract]
public sealed class AzureDevOpsWorkItemDto
{
    [DataMember(Order = 1)]
    public int Id { get; set; }

    [DataMember(Order = 2)]
    public string Title { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string State { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string WorkItemType { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public int Revision { get; set; }

    [DataMember(Order = 6)]
    public double? Estimate { get; set; }

    [DataMember(Order = 7)]
    public string? Description { get; set; }
}

[DataContract]
public sealed class WriteEstimateRequest
{
    [DataMember(Order = 1)]
    public int WorkItemId { get; set; }

    [DataMember(Order = 2)]
    public int? ExpectedRevision { get; set; }

    [DataMember(Order = 3)]
    public double Estimate { get; set; }
}

[DataContract]
public sealed class WriteEstimateReply
{
    [DataMember(Order = 1)]
    public int WorkItemId { get; set; }

    [DataMember(Order = 2)]
    public int Revision { get; set; }
}
