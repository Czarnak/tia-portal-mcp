using TiaMcpServer.Contracts.Project;

namespace TiaMcpServer.Contracts.Vci;

public sealed class VciQueryInfo
{
    public string? WorkspaceName { get; set; }
    public List<ProjectTreeSelectorSegment>? Scope { get; set; }
    public bool WithStatus { get; set; }
    public int? Offset { get; set; }
    public int? Limit { get; set; }
}

public sealed class VciWriteRequestInfo
{
    public string OperationId { get; set; } = string.Empty;
    public string Operation { get; set; } = string.Empty;
    public string? WorkspaceName { get; set; }
    public List<ProjectTreeSelectorSegment>? Scope { get; set; }
    public string? RootPath { get; set; }
    public string? RelativeDirectory { get; set; }
    public string? Format { get; set; }
    public string? RequestId { get; set; }
    public string? BreadcrumbPath { get; set; }
}
