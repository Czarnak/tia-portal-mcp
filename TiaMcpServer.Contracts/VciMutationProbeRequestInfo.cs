namespace TiaMcpServer.Contracts;

/// <summary>
/// One closed-catalogue request for the internal VCI Workspace Phase 1 mutation probe.
/// The caller selects a reviewed <see cref="CaseId"/> and supplies only typed inputs used by
/// that case; it cannot select Siemens members or caller-defined invocation sequences.
/// </summary>
public sealed class VciMutationProbeRequestInfo
{
    public string SchemaVersion { get; set; } = VciMutationProbeContract.SchemaVersion;
    public string RunId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string ScenarioId { get; set; } = string.Empty;
    public string CaseId { get; set; } = string.Empty;
    public string CaseInstanceId { get; set; } = string.Empty;
    public string Mode { get; set; } = string.Empty;
    public string WorkspaceRoot { get; set; } = string.Empty;
    public string? GroupName { get; set; }
    public string? NestedGroupName { get; set; }
    public string? WorkspaceName { get; set; }
    public string? WorkspaceLanguage { get; set; }
    public VciWorkspaceSelectorInfo? Workspace { get; set; }
    public VciEngineeringObjectSelectorInfo? EngineeringObject { get; set; }
    public VciMappingSelectorInfo? Mapping { get; set; }
    public string? RelativeDirectory { get; set; }
    public string? FileName { get; set; }
    public string? FileFormat { get; set; }
    public string? SeedRelativePath { get; set; }
    public string? SynchronizationMode { get; set; }
    public bool RollbackTransaction { get; set; }
    public int MaxGroupDepth { get; set; } = 16;
    public int MaxGroups { get; set; } = 500;
    public int MaxWorkspaces { get; set; } = 500;
    public int MaxMappings { get; set; } = 5000;
    public int MaxEngineeringObjects { get; set; } = 200;
    public int MaxCollectionItems { get; set; } = 5000;
}
