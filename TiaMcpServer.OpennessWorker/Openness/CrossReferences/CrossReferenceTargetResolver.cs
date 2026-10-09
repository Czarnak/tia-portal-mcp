using Siemens.Engineering;
using Siemens.Engineering.CrossReference;
using Siemens.Engineering.HW;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Tags;
using Siemens.Engineering.SW.Types;
using Siemens.Engineering.SW.Units;
using TiaMcpServer.Contracts.CrossReferences;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Plc;
using TiaMcpServer.OpennessWorker.Openness.Project;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.CrossReferences;

using Project = Siemens.Engineering.Project;

/// <summary>
/// A resolved cross-reference target: either a leaf owner's service (one query) or a container
/// whose owners the reader fans out over. Exactly one of <see cref="LeafService"/> and
/// <see cref="Container"/> is set.
/// </summary>
public sealed class ResolvedCrossReferenceTarget
{
    public ResolvedCrossReferenceTarget(CrossReferenceSelectorInfo canonical, CrossReferenceService? leafService, object? container)
    {
        Canonical = canonical;
        LeafService = leafService;
        Container = container;
    }

    /// <summary>The selector with every name spelled as the project spells it.</summary>
    public CrossReferenceSelectorInfo Canonical { get; }

    public CrossReferenceService? LeafService { get; }

    public object? Container { get; }
}

/// <summary>
/// Resolves a <c>browse_project_tree</c> selector (plus an optional tag-table member) to live
/// objects. Children are enumerated exactly as <see cref="ProjectTreeSnapshotWalker"/> emits them
/// and matched as <see cref="ProjectTreeFilter"/> matches them: node type ordinal, name
/// case-insensitive, siblings differing only in case are ambiguous.
/// </summary>
public static class CrossReferenceTargetResolver
{
    public static ResolvedCrossReferenceTarget Resolve(ProjectBase project, CrossReferenceSelectorInfo selector)
    {
        var path = selector?.Path ?? throw Invalid("The cross-reference target needs a path.");
        Device device;
        try
        {
            if (path.Count == 0) throw ProjectTreeSelectionException.Invalid("The cross-reference target path must not be empty.");
            ProjectTreeNodeTypes.Validate(path);
            device = ProjectTreeDeviceSelector.Select(ProjectDeviceEnumerator.Enumerate(project).ToList(), d => d.Name, path[0]);
        }
        catch (ProjectTreeSelectionException exception)
        {
            throw new WorkerOperationException(exception.Category, exception.Message);
        }

        var canonical = new CrossReferenceSelectorInfo();
        canonical.Path.Add(Segment(ProjectTreeNodeTypes.Device, device.Name));
        object current = device;
        for (var index = 1; index < path.Count; index++)
        {
            var match = MatchOne(Children(current), path[index].NodeType, path[index].Name);
            canonical.Path.Add(Segment(match.NodeType, match.Name));
            current = match.Value;
        }

        var lastType = canonical.Path[canonical.Path.Count - 1].NodeType;
        if (selector!.Member is not null)
        {
            if (current is not PlcTagTable table)
                throw Invalid("A member target needs a path ending at a TagTable.");
            var member = MatchOne(Members(table, selector.Member.Kind), selector.Member.Kind, selector.Member.Name);
            canonical.Member = new CrossReferenceMemberSelectorInfo { Kind = member.NodeType, Name = member.Name };
            return Leaf(canonical, (IEngineeringServiceProvider)member.Value);
        }

        return ProjectTreeNodeTypes.BlockLeaves.Contains(lastType) || lastType == ProjectTreeNodeTypes.Type
            ? Leaf(canonical, (IEngineeringServiceProvider)current)
            : new ResolvedCrossReferenceTarget(canonical, null, current);
    }

    private static ResolvedCrossReferenceTarget Leaf(CrossReferenceSelectorInfo canonical, IEngineeringServiceProvider owner)
    {
        CrossReferenceService? service;
        try { service = owner.GetService<CrossReferenceService>(); }
        catch (Exception ex) when (IsRecoverable(ex))
        {
            throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed,
                "The cross-reference service of the selected target could not be read.");
        }

        return service is null
            ? throw new WorkerOperationException(WorkerFailureCategories.TargetKindUnsupported,
                "The selected target does not provide cross-references.")
            : new ResolvedCrossReferenceTarget(canonical, service, null);
    }

    private static Candidate MatchOne(List<Candidate> candidates, string nodeType, string name)
    {
        var matches = candidates.Where(c => string.Equals(c.NodeType, nodeType, StringComparison.Ordinal)
            && string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();
        return matches.Count switch
        {
            1 => matches[0],
            0 => throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound,
                "The cross-reference target selector did not resolve to a target."),
            _ => throw new WorkerOperationException(WorkerFailureCategories.TargetAmbiguous,
                "The cross-reference target selector resolved to multiple targets.")
        };
    }

    // Mirrors ProjectTreeSnapshotWalker: same collections, same node types, and a child whose name
    // cannot be read is skipped (the walker skips it) rather than failing the resolution.
    private static List<Candidate> Children(object parent)
    {
        var children = new List<Candidate>();
        switch (parent)
        {
            case Device device:
                foreach (var software in PlcSoftwareLocator.FindInDevice(device))
                    Add(children, ProjectTreeNodeTypes.PlcSoftware, () => software.Name, software);
                break;
            case PlcSoftware software:
                Add(children, ProjectTreeNodeTypes.BlockFolder, () => software.BlockGroup.Name, software.BlockGroup);
                Add(children, ProjectTreeNodeTypes.TagTableFolder, () => software.TagTableGroup.Name, software.TagTableGroup);
                Add(children, ProjectTreeNodeTypes.TypeFolder, () => software.TypeGroup.Name, software.TypeGroup);
                PlcUnitProvider? provider = null;
                try { provider = software.GetService<PlcUnitProvider>(); }
                catch (Exception ex) when (IsRecoverable(ex)) { }
                if (provider is not null)
                    foreach (PlcUnit unit in provider.UnitGroup.Units)
                        Add(children, ProjectTreeNodeTypes.SoftwareUnit, () => unit.Name, unit);
                break;
            case PlcUnit unit:
                Add(children, ProjectTreeNodeTypes.BlockFolder, () => unit.BlockGroup.Name, unit.BlockGroup);
                Add(children, ProjectTreeNodeTypes.TagTableFolder, () => unit.TagTableGroup.Name, unit.TagTableGroup);
                Add(children, ProjectTreeNodeTypes.TypeFolder, () => unit.TypeGroup.Name, unit.TypeGroup);
                break;
            case PlcSystemBlockGroup group:
                foreach (PlcBlock block in group.Blocks) Add(children, BlockNodeType(block), () => block.Name, block);
                foreach (PlcSystemBlockGroup child in group.Groups)
                    Add(children, ProjectTreeNodeTypes.SystemBlockFolder, () => child.Name, child);
                break;
            case PlcBlockGroup group:
                foreach (PlcBlock block in group.Blocks) Add(children, BlockNodeType(block), () => block.Name, block);
                foreach (PlcBlockGroup child in group.Groups)
                    Add(children, ProjectTreeNodeTypes.BlockFolder, () => child.Name, child);
                if (group is PlcBlockSystemGroup system)
                    foreach (PlcSystemBlockGroup child in system.SystemBlockGroups)
                        Add(children, ProjectTreeNodeTypes.SystemBlockFolder, () => child.Name, child);
                break;
            case PlcTagTableGroup group:
                foreach (PlcTagTable table in group.TagTables)
                    Add(children, ProjectTreeNodeTypes.TagTable, () => table.Name, table);
                foreach (PlcTagTableGroup child in group.Groups)
                    Add(children, ProjectTreeNodeTypes.TagTableFolder, () => child.Name, child);
                break;
            case PlcTypeGroup group:
                foreach (PlcType type in group.Types) Add(children, ProjectTreeNodeTypes.Type, () => type.Name, type);
                foreach (PlcTypeGroup child in group.Groups)
                    Add(children, ProjectTreeNodeTypes.TypeFolder, () => child.Name, child);
                break;
        }
        return children;
    }

    private static List<Candidate> Members(PlcTagTable table, string kind)
    {
        var members = new List<Candidate>();
        switch (kind)
        {
            case CrossReferenceMemberKinds.Tag:
                foreach (PlcTag tag in table.Tags) Add(members, kind, () => tag.Name, tag);
                break;
            case CrossReferenceMemberKinds.SystemConstant:
                foreach (PlcSystemConstant constant in table.SystemConstants) Add(members, kind, () => constant.Name, constant);
                break;
            case CrossReferenceMemberKinds.UserConstant:
                foreach (PlcUserConstant constant in table.UserConstants) Add(members, kind, () => constant.Name, constant);
                break;
            default:
                throw Invalid($"Unsupported member kind. Allowed kinds: {string.Join(", ", CrossReferenceMemberKinds.All)}.");
        }
        return members;
    }

    private static string BlockNodeType(PlcBlock block) => block switch
    {
        OB => ProjectTreeNodeTypes.Ob,
        FB => ProjectTreeNodeTypes.Fb,
        FC => ProjectTreeNodeTypes.Fc,
        GlobalDB => ProjectTreeNodeTypes.GlobalDb,
        InstanceDB => ProjectTreeNodeTypes.InstanceDb,
        ArrayDB => ProjectTreeNodeTypes.ArrayDb,
        _ => ProjectTreeNodeTypes.Block
    };

    private static void Add(List<Candidate> candidates, string nodeType, Func<string> name, object value)
    {
        try { candidates.Add(new Candidate(nodeType, name(), value)); }
        catch (Exception ex) when (IsRecoverable(ex)) { }
    }

    // NonRecoverableException (session loss) always propagates, as in CrossReferenceReader.
    private static bool IsRecoverable(Exception ex) => ex is EngineeringException && ex is not NonRecoverableException;

    private static ProjectTreeSelectorSegment Segment(string nodeType, string name)
        => new ProjectTreeSelectorSegment { NodeType = nodeType, Name = name };

    private static WorkerOperationException Invalid(string message)
        => new WorkerOperationException(WorkerFailureCategories.InvalidSelector, message);

    private sealed class Candidate
    {
        public Candidate(string nodeType, string name, object value)
        {
            NodeType = nodeType;
            Name = name;
            Value = value;
        }

        public string NodeType { get; }
        public string Name { get; }
        public object Value { get; }
    }
}
