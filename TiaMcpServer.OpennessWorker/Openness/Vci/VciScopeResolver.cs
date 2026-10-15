using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.Scripts;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Tags;
using Siemens.Engineering.SW.TechnologicalObjects;
using Siemens.Engineering.SW.Types;
using Siemens.Engineering.SW.Units;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Vci;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Project;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.Vci;

public sealed class VciLeaf
{
    public VciLeaf(IEngineeringObject @object, List<ProjectTreeSelectorSegment> path, bool isPlc)
    {
        Object = @object;
        Path = path;
        IsPlc = isPlc;
    }
    public IEngineeringObject Object { get; }
    public List<ProjectTreeSelectorSegment> Path { get; }
    public bool IsPlc { get; }
}

public static class VciScopeResolver
{
    public static IReadOnlyList<VciLeaf> Expand(ProjectBase project, IReadOnlyList<ProjectTreeSelectorSegment> scope)
    {
        try
        {
            if (scope is null)
                throw ProjectTreeSelectionException.Invalid("scope is required.");
            VciScope.Validate(scope);
            IEngineeringObject selected = ProjectTreeDeviceSelector.Select(
                ProjectDeviceEnumerator.Enumerate(project).ToList(), d => d.Name, scope[0]);
            var path = new List<ProjectTreeSelectorSegment> { Segment(selected)! };
            for (var index = 1; index < scope.Count; index++)
            {
                var requested = scope[index];
                var matches = Children(selected).Where(child =>
                {
                    var segment = Segment(child);
                    return segment is not null && segment.NodeType == requested.NodeType
                        && string.Equals(segment.Name, requested.Name, StringComparison.OrdinalIgnoreCase);
                }).ToList();
                if (matches.Count != 1)
                    throw new WorkerOperationException(matches.Count == 0
                        ? WorkerFailureCategories.TargetNotFound : WorkerFailureCategories.TargetAmbiguous,
                        matches.Count == 0 ? "The selected scope was not found." : "The selected scope is ambiguous.");
                selected = matches[0];
                path.Add(Segment(selected)!);
            }
            var leaves = new List<VciLeaf>();
            ExpandNode(selected, path, leaves, new List<IEngineeringObject>());
            return leaves;
        }
        catch (ProjectTreeSelectionException ex)
        {
            throw new WorkerOperationException(ex.Category,
                "Invalid scope: " + ex.Message.Replace("startSelector", "scope"));
        }
    }

    public static List<ProjectTreeSelectorSegment>? PathOf(IEngineeringObject @object)
    {
        try
        {
            if (Segment(@object) is null) return null;
            var path = new List<ProjectTreeSelectorSegment>();
            var seen = new List<IEngineeringObject>();
            IEngineeringObject? current = @object;
            while (current is not null)
            {
                if (seen.Any(previous => ReferenceEquals(previous, current))) return null;
                seen.Add(current);
                var segment = Segment(current);
                if (segment is not null) path.Add(segment);
                if (current is Device)
                {
                    path.Reverse();
                    return path;
                }
                current = current.Parent;
            }
            return null;
        }
        catch (NonRecoverableException) { throw; }
        catch (EngineeringObjectDisposedException) { return null; }
    }

    private static void ExpandNode(IEngineeringObject node, List<ProjectTreeSelectorSegment> path,
        List<VciLeaf> leaves, List<IEngineeringObject> ancestors)
    {
        if (ancestors.Any(parent => ReferenceEquals(parent, node)))
            throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed, "A cycle prevents complete scope expansion.");
        if (VciScope.IsLeaf(path[path.Count - 1].NodeType))
        {
            leaves.Add(new VciLeaf(node, path, node is not HmiScriptModule));
            return;
        }
        ancestors.Add(node);
        foreach (var child in Children(node))
        {
            var childPath = new List<ProjectTreeSelectorSegment>(path) { Segment(child)! };
            ExpandNode(child, childPath, leaves, ancestors);
        }
        ancestors.RemoveAt(ancestors.Count - 1);
    }

    private static IEnumerable<IEngineeringObject> Children(IEngineeringObject node)
    {
        switch (node)
        {
            case Device device:
                var software = SoftwareIn(device.DeviceItems).ToList();
                if (software.Count == 0)
                    throw new WorkerOperationException(WorkerFailureCategories.TargetKindUnsupported,
                        "The scope device has no supported PLC or Unified HMI software.");
                foreach (var child in software) yield return child;
                break;
            case PlcSoftware plc:
                yield return plc.BlockGroup;
                yield return plc.TagTableGroup;
                yield return plc.TypeGroup;
                yield return plc.TechnologicalObjectGroup;
                break;
            case PlcBlockGroup blocks:
                foreach (PlcBlock block in blocks.Blocks) yield return block;
                foreach (PlcBlockGroup group in blocks.Groups) yield return group;
                break;
            case PlcTagTableGroup tags:
                foreach (PlcTagTable table in tags.TagTables) yield return table;
                foreach (PlcTagTableGroup group in tags.Groups) yield return group;
                break;
            case PlcTypeGroup types:
                foreach (PlcType type in types.Types) yield return type;
                foreach (PlcTypeGroup group in types.Groups) yield return group;
                break;
            case TechnologicalInstanceDBGroup technologies:
                foreach (TechnologicalInstanceDB technology in technologies.TechnologicalObjects) yield return technology;
                foreach (TechnologicalInstanceDBGroup group in technologies.Groups) yield return group;
                break;
            case HmiSoftware hmi:
                foreach (HmiScriptModule script in hmi.Scripts) yield return script;
                break;
        }
    }

    // Shared software locators swallow or wrap discovery failures; VCI requires complete discovery and fatal propagation.
    private static IEnumerable<IEngineeringObject> SoftwareIn(DeviceItemComposition items)
    {
        foreach (DeviceItem item in items)
        {
            var software = item.GetService<SoftwareContainer>()?.Software;
            if (software is PlcSoftware plc) yield return plc;
            else if (software is HmiSoftware hmi) yield return hmi;
            foreach (var child in SoftwareIn(item.DeviceItems)) yield return child;
        }
    }

    private static ProjectTreeSelectorSegment? Segment(IEngineeringObject node) => node switch
    {
        Device device => At(ProjectTreeNodeTypes.Device, device.Name),
        PlcSoftware plc => At(ProjectTreeNodeTypes.PlcSoftware, plc.Name),
        PlcUnit unit => At(ProjectTreeNodeTypes.SoftwareUnit, unit.Name),
        PlcSystemBlockGroup system => At(ProjectTreeNodeTypes.SystemBlockFolder, system.Name),
        PlcBlockGroup blocks => At(ProjectTreeNodeTypes.BlockFolder, blocks.Name),
        PlcTagTableGroup tags => At(ProjectTreeNodeTypes.TagTableFolder, tags.Name),
        PlcTypeGroup types => At(ProjectTreeNodeTypes.TypeFolder, types.Name),
        TechnologicalInstanceDBGroup technologies => At(VciScope.TechnologyObjectFolder, technologies.Name),
        TechnologicalInstanceDB technology => At(VciScope.TechnologyObject, technology.Name),
        OB block => At(ProjectTreeNodeTypes.Ob, block.Name),
        FB block => At(ProjectTreeNodeTypes.Fb, block.Name),
        FC block => At(ProjectTreeNodeTypes.Fc, block.Name),
        GlobalDB block => At(ProjectTreeNodeTypes.GlobalDb, block.Name),
        InstanceDB block => At(ProjectTreeNodeTypes.InstanceDb, block.Name),
        ArrayDB block => At(ProjectTreeNodeTypes.ArrayDb, block.Name),
        PlcBlock block => At(ProjectTreeNodeTypes.Block, block.Name),
        PlcTagTable table => At(ProjectTreeNodeTypes.TagTable, table.Name),
        PlcType type => At(ProjectTreeNodeTypes.Type, type.Name),
        HmiSoftware hmi => At(VciScope.HmiSoftware, hmi.Name),
        HmiScriptModule script => At(VciScope.ScriptModule, script.Name),
        _ => null
    };

    private static ProjectTreeSelectorSegment At(string type, string name)
        => new ProjectTreeSelectorSegment { NodeType = type, Name = name };
}
