using System.ComponentModel;
using System.Text.Json.Serialization;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.OperationBatches;

namespace TiaMcpServer.Vci;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class VciOperationRequest : IOperationBatchItem
{
    public string OperationId { get; set; } = string.Empty;
    public string Operation { get; set; } = string.Empty;
    [Description("Optional bound standalone project path assertion; never opens or switches a project.")]
    public string? ProjectPath { get; set; }
    public string? WorkspaceName { get; set; }
    [Description("VCI selector segments {nodeType,name} starting at Device. Software units and system block folders are unsupported.")]
    public List<ProjectTreeSelectorSegment>? Scope { get; set; }
    [Description("list_mappings only: include GetStatus evidence; about 0.5 seconds per returned mapping. Default false.")]
    public bool? WithStatus { get; set; }
    [Description("list_mappings only: plain offset paging, default 0; no snapshot.")]
    public int? Offset { get; set; }
    [Description("list_mappings only: 1 to 200 rows, default 100.")]
    public int? Limit { get; set; }
    public string? RootPath { get; set; }
    public string? RelativeDirectory { get; set; }
    public string? Format { get; set; }
}
