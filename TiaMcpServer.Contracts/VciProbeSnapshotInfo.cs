using System.Collections.Generic;

namespace TiaMcpServer.Contracts;

/// <summary>
/// Envelope for whichever bounded snapshot a probe case captured. Only the member(s) relevant to
/// the case's kind of read are populated; the rest stay at their empty/null default.
/// </summary>
public sealed class VciProbeSnapshotInfo
{
    /// <summary>
    /// Ordered normalized member observations that produced this snapshot, including typed
    /// member-level exception evidence. Empty when the case has no member observations.
    /// </summary>
    public List<VciProbeMemberObservationInfo> Members { get; set; } = new();

    /// <summary>Populated by case <c>R-SVC</c>.</summary>
    public VciProbeServiceSnapshotInfo? Service { get; set; }

    /// <summary>Populated by cases <c>R-GRP</c> and <c>R-CANARY</c>.</summary>
    public List<VciProbeGroupSnapshotInfo> Groups { get; set; } = new();

    /// <summary>Populated by case <c>R-WS</c>.</summary>
    public List<VciProbeWorkspaceSnapshotInfo> Workspaces { get; set; } = new();

    /// <summary>Populated by case <c>R-MAP</c>.</summary>
    public List<VciProbeMappingSnapshotInfo> Mappings { get; set; } = new();

    /// <summary>Populated by format-discovery cases (e.g. <c>R-FMT</c>).</summary>
    public List<VciProbeCandidateSnapshotInfo> Candidates { get; set; } = new();

    /// <summary>Runtime type of the raw format collection observed for an <c>R-FMT</c> case.</summary>
    public string? CandidateCollectionRuntimeType { get; set; }
}

/// <summary>Bounded snapshot of the VCI service and its root workspace group.</summary>
public sealed class VciProbeServiceSnapshotInfo
{
    /// <summary>True when <c>Project.GetService&lt;VersionControlInterface&gt;()</c> returned non-null.</summary>
    public bool ServiceAvailable { get; set; }

    /// <summary>True when the service's root <c>WorkspaceGroup</c> was reachable.</summary>
    public bool RootGroupAvailable { get; set; }

    /// <summary>Immediate child-group count of the root workspace group.</summary>
    public int RootGroupCount { get; set; }
}

/// <summary>
/// One workspace group observed while walking the group tree. <see cref="EnumerationIndex"/>
/// preserves the order the worker observed it in; <see cref="CanonicalKey"/> is a stable key for
/// matching the same group across two independently ordered observations (e.g. the two-session
/// live comparison). The worker must never sort the raw observed collection — order is evidence.
/// </summary>
public sealed class VciProbeGroupSnapshotInfo
{
    /// <summary>Zero-based position in the order the worker enumerated this group.</summary>
    public int EnumerationIndex { get; set; }

    /// <summary>Stable key for matching this group across independently ordered observations.</summary>
    public string CanonicalKey { get; set; } = string.Empty;

    /// <summary>Group name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Depth from the workspace group root (root's children are depth 1).</summary>
    public int Depth { get; set; }

    /// <summary><see cref="CanonicalKey"/> of the parent group, or null for a root-level group.</summary>
    public string? ParentCanonicalKey { get; set; }

    /// <summary>Immediate child-group count.</summary>
    public int ChildGroupCount { get; set; }

    /// <summary>Immediate workspace count.</summary>
    public int WorkspaceCount { get; set; }
}

/// <summary>
/// One workspace observed while enumerating a group's workspaces. <see cref="EnumerationIndex"/>
/// and <see cref="CanonicalKey"/> follow the same order-preservation contract as
/// <see cref="VciProbeGroupSnapshotInfo"/>.
/// </summary>
public sealed class VciProbeWorkspaceSnapshotInfo
{
    /// <summary>Zero-based position in the order the worker enumerated this workspace.</summary>
    public int EnumerationIndex { get; set; }

    /// <summary>Stable key for matching this workspace across independently ordered observations.</summary>
    public string CanonicalKey { get; set; } = string.Empty;

    /// <summary>Workspace name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Workspace root path.</summary>
    public string? RootPath { get; set; }

    /// <summary>Workspace comment.</summary>
    public string? Comment { get; set; }

    /// <summary>Workspace language.</summary>
    public string? WorkspaceLanguage { get; set; }

    /// <summary>Workspace global library path.</summary>
    public string? GlobalLibraryPath { get; set; }

    /// <summary>Workspace's <c>DeleteUnusedTypeVersionFromLibrary</c> setting.</summary>
    public bool DeleteUnusedTypeVersionFromLibrary { get; set; }

    /// <summary>Number of mapped objects observed for this workspace.</summary>
    public int MappedObjectCount { get; set; }
}

/// <summary>
/// One mapping observed while enumerating a workspace's mapped objects. <see cref="EnumerationIndex"/>
/// and <see cref="CanonicalKey"/> follow the same order-preservation contract as
/// <see cref="VciProbeGroupSnapshotInfo"/>.
/// </summary>
public sealed class VciProbeMappingSnapshotInfo
{
    /// <summary>Zero-based position in the order the worker enumerated this mapping.</summary>
    public int EnumerationIndex { get; set; }

    /// <summary>Stable key for matching this mapping across independently ordered observations.</summary>
    public string CanonicalKey { get; set; } = string.Empty;

    /// <summary>Selector identifying the mapping's workspace, engineering object, and file location.</summary>
    public VciMappingSelectorInfo Selector { get; set; } = new();

    /// <summary>String rendering of <c>MappedObject.GetStatus()</c>, when observed.</summary>
    public string? Status { get; set; }

    /// <summary>String rendering of the <c>MappedObject.Status</c> property, when observed.</summary>
    public string? StatusProperty { get; set; }

    /// <summary>String rendering of <c>MappedObject.GetStatus()</c>, when observed.</summary>
    public string? GetStatus { get; set; }

    /// <summary>String rendering of <c>MappedObject.GetChildStatus()</c>, when observed.</summary>
    public string? ChildStatus { get; set; }
}

/// <summary>
/// One candidate observed while enumerating a bounded discovery result (e.g. a supported file
/// format). <see cref="EnumerationIndex"/> and <see cref="CanonicalKey"/> follow the same
/// order-preservation contract as <see cref="VciProbeGroupSnapshotInfo"/>.
/// </summary>
public sealed class VciProbeCandidateSnapshotInfo
{
    /// <summary>Zero-based position in the order the worker enumerated this candidate.</summary>
    public int EnumerationIndex { get; set; }

    /// <summary>Stable key for matching this candidate across independently ordered observations.</summary>
    public string CanonicalKey { get; set; } = string.Empty;

    /// <summary>Human-readable description of the candidate (e.g. a file format's display name).</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Runtime type of this raw candidate item, including for unsupported values.</summary>
    public string RuntimeTypeName { get; set; } = string.Empty;

    /// <summary>True when the raw candidate item was null, distinct from an empty string.</summary>
    public bool IsNull { get; set; }
}
