using System.Collections.Generic;

namespace TiaMcpServer.Contracts;

/// <summary>
/// One segment of a group path from the VCI workspace-group root down to a specific group.
/// Carries both a positional index and the group's name so a resolver can locate a group by
/// either signal.
/// </summary>
public sealed class VciGroupPathSegmentInfo
{
    /// <summary>Zero-based sibling index within the parent group's child-group composition.</summary>
    public int Index { get; set; }

    /// <summary>Name of the group at this level of the group hierarchy.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Zero-based occurrence among siblings with the same <see cref="Name"/>.</summary>
    public int SameNameOrdinal { get; set; }
}

/// <summary>
/// Identifies exactly one VCI workspace by the complete group-segment path from the workspace
/// group root to its owning group, plus the workspace's own identity and canonical root path.
///
/// <para>
/// Provisional and internal to the Phase 1 read-only probe. Not a public tool contract and never
/// bound into a safety token.
/// </para>
/// </summary>
public sealed class VciWorkspaceSelectorInfo
{
    /// <summary>Complete ordered group segments from the workspace group root to the owning group.</summary>
    public List<VciGroupPathSegmentInfo> GroupPath { get; set; } = new();

    /// <summary>Exact workspace name within its owning group.</summary>
    public string WorkspaceName { get; set; } = string.Empty;

    /// <summary>Canonical (normalized) root path reported by the workspace, when observed.</summary>
    public string? CanonicalRootPath { get; set; }
}

/// <summary>
/// One segment of a typed structural path to an engineering object (e.g. a PLC block, a type, or a
/// folder), used as a stable fallback when no V21 identifier is available for the object.
/// </summary>
public sealed class VciEngineeringObjectPathSegmentInfo
{
    /// <summary>Zero-based sibling index within the parent composition at this level.</summary>
    public int Index { get; set; }

    /// <summary>Name of the object at this level of the structural path.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Engineering object type at this level (e.g. Block, PlcType, Folder).</summary>
    public string ObjectType { get; set; } = string.Empty;
}

/// <summary>
/// Identifies exactly one engineering object for a bounded VCI probe read. Prefers the stable V21
/// object identifier when the installed API exposes one; always carries a typed structural path
/// and a content/identity fingerprint as a cross-check and fallback.
///
/// <para>
/// Provisional and internal to the Phase 1 read-only probe. Not a public tool contract and never
/// bound into a safety token.
/// </para>
/// </summary>
public sealed class VciEngineeringObjectSelectorInfo
{
    /// <summary>Stable V21 object identifier, when the installed API version exposes one.</summary>
    public string? StableIdentifier { get; set; }

    /// <summary>Typed structural path from a resolvable root to the object.</summary>
    public List<VciEngineeringObjectPathSegmentInfo> StructuralPath { get; set; } = new();

    /// <summary>Content/identity fingerprint captured at selector-resolution time.</summary>
    public string? Fingerprint { get; set; }
}

/// <summary>
/// Identifies exactly one VCI mapping by its owning workspace, its mapped engineering object, and
/// its normalized file location within the workspace.
///
/// <para>
/// Provisional and internal to the Phase 1 read-only probe. Not a public tool contract and never
/// bound into a safety token.
/// </para>
/// </summary>
public sealed class VciMappingSelectorInfo
{
    /// <summary>The workspace that owns this mapping.</summary>
    public VciWorkspaceSelectorInfo Workspace { get; set; } = new();

    /// <summary>The engineering object this mapping projects to a file.</summary>
    public VciEngineeringObjectSelectorInfo EngineeringObject { get; set; } = new();

    /// <summary>Normalized relative directory under the workspace root, when observed.</summary>
    public string? RelativeDirectory { get; set; }

    /// <summary>Mapped file name, when observed.</summary>
    public string? FileName { get; set; }

    /// <summary>Mapped file format identifier, when observed.</summary>
    public string? Format { get; set; }
}
