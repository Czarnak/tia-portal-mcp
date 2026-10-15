using TiaMcpServer.Contracts.Project;

namespace TiaMcpServer.Contracts.Vci;

public sealed class VciWorkspaceListInfo
{
    public List<VciWorkspaceInfo> Workspaces { get; set; } = new();
}

public sealed class VciWorkspaceInfo
{
    public string Name { get; set; } = string.Empty;
    public string? RootPath { get; set; }
    public int MappingCount { get; set; }
}

public sealed class VciMappingListInfo
{
    public int Total { get; set; }
    public int Offset { get; set; }
    public int Limit { get; set; }
    public List<VciMappingInfo> Mappings { get; set; } = new();
}

public sealed class VciMappingInfo
{
    public VciFileKey FileKey { get; set; } = new(string.Empty, string.Empty);
    public string Format { get; set; } = string.Empty;
    public List<ProjectTreeSelectorSegment>? Path { get; set; }
    public VciStatusInfo? Status { get; set; }
}

public sealed class VciStatusInfo
{
    public string CompareState { get; set; } = string.Empty;
    public string? Details { get; set; }
}

public sealed class VciSupportedFormatsInfo
{
    public List<string> Formats { get; set; } = new();
}

public sealed class VciPlanInfo
{
    public int Leaves { get; set; }
    public int MappedLeaves { get; set; }
    public int FormatFallbacks { get; set; }
    public string? WorkspaceRootPath { get; set; }
    public string? WorkspaceConflict { get; set; }
    public List<string> KeyCollisions { get; set; } = new();
    public List<string> Unrepresentable { get; set; } = new();
    public string? Unverifiable { get; set; }
}

public sealed class VciWriteOutcomeInfo
{
    public int Leaves { get; set; }
    public int Succeeded { get; set; }
    public int Failed { get; set; }
    public VciSkipCountsInfo Skipped { get; set; } = new();
    public int FormatFallbacks { get; set; }
    public VciLastAttemptedInfo? LastAttempted { get; set; }
    public bool ProjectModified { get; set; }
    public List<string> Notes { get; set; } = new();
}

public sealed class VciSkipCountsInfo
{
    public int Equal { get; set; }
    public int AlreadyMapped { get; set; }
    public int Conflict { get; set; }
    public int TargetChanged { get; set; }
}
