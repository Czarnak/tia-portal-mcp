namespace TiaMcpServer.Contracts;

/// <summary>
/// Request envelope for one internal <c>probe_vci_read_contract</c> case invocation. Carried on
/// <see cref="WorkerRequest.VciProbe"/>; forwarded only by that operation.
///
/// <para>
/// Kept as a single nested request rather than another set of flat <see cref="WorkerRequest"/>
/// fields, since the Phase 1 read-only probe is internal-only and its shape is expected to evolve
/// across the plan's later tasks without touching the rest of the flat request surface.
/// </para>
/// </summary>
public sealed class VciProbeRequestInfo
{
    /// <summary>Wire schema version. Must equal <see cref="VciReadProbeContract.SchemaVersion"/>.</summary>
    public string SchemaVersion { get; set; } = VciReadProbeContract.SchemaVersion;

    /// <summary>Identifier shared by every case invocation in one live-harness run.</summary>
    public string RunId { get; set; } = string.Empty;

    /// <summary>Identifier of the worker session (process) that observed this case.</summary>
    public string SessionId { get; set; } = string.Empty;

    /// <summary>One of <see cref="VciReadProbeContract.CaseIds"/>.</summary>
    public string CaseId { get; set; } = string.Empty;

    /// <summary>Identifier unique to this one case invocation, distinct from <see cref="CaseId"/>.</summary>
    public string CaseInstanceId { get; set; } = string.Empty;

    /// <summary>Optional captured name evidence for the object this case targets.</summary>
    public string? TargetName { get; set; }

    /// <summary>Workspace selector. Required for case <c>R-FMT</c>; optional otherwise.</summary>
    public VciWorkspaceSelectorInfo? Workspace { get; set; }

    /// <summary>Engineering-object selector. Required for case <c>R-FMT</c>; optional otherwise.</summary>
    public VciEngineeringObjectSelectorInfo? EngineeringObject { get; set; }

    /// <summary>
    /// Absolute path to a separately supplied, already-open secondary project. Used only by
    /// <c>N-FMT-FOREIGN</c>; that case reports outcome <c>not_observable</c> when omitted.
    /// </summary>
    public string? SecondaryProjectPath { get; set; }

    /// <summary>Maximum group-tree recursion depth the worker may traverse for this case.</summary>
    public int MaxGroupDepth { get; set; } = 16;

    /// <summary>Maximum number of groups the worker may enumerate for this case.</summary>
    public int MaxGroups { get; set; } = 500;

    /// <summary>Maximum number of workspaces the worker may enumerate for this case.</summary>
    public int MaxWorkspaces { get; set; } = 500;

    /// <summary>Maximum number of mappings the worker may enumerate for this case.</summary>
    public int MaxMappings { get; set; } = 5000;

    /// <summary>Maximum number of engineering objects the worker may resolve for this case.</summary>
    public int MaxEngineeringObjects { get; set; } = 200;

    /// <summary>Maximum number of items the worker may read from any single collection for this case.</summary>
    public int MaxCollectionItems { get; set; } = 5000;
}
