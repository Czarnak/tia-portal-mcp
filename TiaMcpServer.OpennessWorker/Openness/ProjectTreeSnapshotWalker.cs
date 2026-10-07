using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Tags;
using Siemens.Engineering.SW.Types;
using Siemens.Engineering.SW.Units;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

/// <summary>
/// Materializes a typed tree snapshot without legacy presentation paths. A node that cannot be read
/// is left out of the tree and recorded in <see cref="ProjectTreeSelectionResult.Skipped"/> so write
/// planning can treat the surrounding state as unverifiable instead of silently incomplete.
/// </summary>
public sealed class ProjectTreeSnapshotWalker
{
    private static readonly IReadOnlyList<ProjectTreeSelectorSegment> NoParent = Array.Empty<ProjectTreeSelectorSegment>();

    public ProjectTreeSelectionResult WalkSnapshot(Project project, IReadOnlyList<ProjectTreeSelectorSegment>? startSelector, int? depth)
    {
        ProjectTreeNodeTypes.Validate(startSelector);
        var devices = ProjectDeviceEnumerator.Enumerate(project).Cast<Device>().ToList();
        var skipped = new List<ProjectTreeSkippedNodeInfo>();

        if (startSelector is null)
        {
            var roots = devices.Select(device => WalkDevice(device, skipped)).ToList();
            return ProjectTreeFilter.Apply(roots, startSelector: null, depth, skipped);
        }

        var selectedDevice = ProjectTreeDeviceSelector.Select(devices, device => device.Name, startSelector[0]);
        var selectedRoot = WalkDevice(selectedDevice, skipped);
        return ProjectTreeFilter.Apply(new List<ProjectTreeNode> { selectedRoot }, startSelector, depth, skipped);
    }

    private static IReadOnlyList<ProjectTreeSelectorSegment> Down(
        IReadOnlyList<ProjectTreeSelectorSegment> parent,
        string nodeType,
        string name)
    {
        var path = new List<ProjectTreeSelectorSegment>(parent.Count + 1);
        path.AddRange(parent);
        path.Add(new ProjectTreeSelectorSegment { NodeType = nodeType, Name = name });
        return path;
    }

    private static void Skip(
        List<ProjectTreeSkippedNodeInfo> skipped,
        IReadOnlyList<ProjectTreeSelectorSegment> parent,
        string nodeType,
        string what,
        string container,
        EngineeringException ex)
    {
        Console.Error.WriteLine($"Skipping {what} while walking {container}: {ex.Message}");
        skipped.Add(new ProjectTreeSkippedNodeInfo
        {
            ParentPath = parent.Select(s => new ProjectTreeSelectorSegment { NodeType = s.NodeType, Name = s.Name }).ToList(),
            NodeType = nodeType,
            Reason = ex.Message
        });
    }

    private static ProjectTreeNode WalkDevice(Device device, List<ProjectTreeSkippedNodeInfo> skipped)
    {
        var path = Down(NoParent, ProjectTreeNodeTypes.Device, device.Name);
        var children = new List<ProjectTreeNode>();
        foreach (var plcSoftware in PlcSoftwareLocator.FindInDevice(device))
        {
            children.Add(WalkPlcSoftware(plcSoftware, path, skipped));
        }
        return new ProjectTreeNode { Name = device.Name, NodeType = ProjectTreeNodeTypes.Device, Details = null, Children = children };
    }

    private static ProjectTreeNode WalkPlcSoftware(
        PlcSoftware plcSoftware,
        IReadOnlyList<ProjectTreeSelectorSegment> parent,
        List<ProjectTreeSkippedNodeInfo> skipped)
    {
        var path = Down(parent, ProjectTreeNodeTypes.PlcSoftware, plcSoftware.Name);
        var children = new List<ProjectTreeNode>
        {
            WalkBlockGroup(plcSoftware.BlockGroup, softwareUnitName: null, path, skipped),
            WalkTagTableGroup(plcSoftware.TagTableGroup, path, skipped),
            WalkTypeGroup(plcSoftware.TypeGroup, path, skipped)
        };
        children.AddRange(WalkSoftwareUnits(plcSoftware, path, skipped));
        return new ProjectTreeNode { Name = plcSoftware.Name, NodeType = ProjectTreeNodeTypes.PlcSoftware, Details = null, Children = children };
    }

    private static IEnumerable<ProjectTreeNode> WalkSoftwareUnits(
        PlcSoftware plcSoftware,
        IReadOnlyList<ProjectTreeSelectorSegment> parent,
        List<ProjectTreeSkippedNodeInfo> skipped)
    {
        PlcUnitProvider? unitProvider = null;
        try { unitProvider = plcSoftware.GetService<PlcUnitProvider>(); }
        catch (EngineeringException ex) { Skip(skipped, parent, ProjectTreeNodeTypes.SoftwareUnit, "software units", $"PLC software '{plcSoftware.Name}'", ex); }
        if (unitProvider is null) yield break;

        foreach (PlcUnit unit in unitProvider.UnitGroup.Units)
        {
            var unitPath = Down(parent, ProjectTreeNodeTypes.SoftwareUnit, unit.Name);
            yield return new ProjectTreeNode
            {
                Name = unit.Name,
                NodeType = ProjectTreeNodeTypes.SoftwareUnit,
                Details = null,
                Children = new List<ProjectTreeNode>
                {
                    WalkBlockGroup(unit.BlockGroup, unit.Name, unitPath, skipped),
                    WalkTagTableGroup(unit.TagTableGroup, unitPath, skipped),
                    WalkTypeGroup(unit.TypeGroup, unitPath, skipped)
                }
            };
        }
    }

    private static ProjectTreeNode WalkBlockGroup(
        PlcBlockGroup group,
        string? softwareUnitName,
        IReadOnlyList<ProjectTreeSelectorSegment> parent,
        List<ProjectTreeSkippedNodeInfo> skipped)
    {
        var path = Down(parent, ProjectTreeNodeTypes.BlockFolder, group.Name);
        var container = $"block group '{group.Name}'";
        var children = new List<ProjectTreeNode>();
        foreach (PlcBlock block in group.Blocks)
        {
            try { children.Add(BuildBlockNode(block, softwareUnitName, isSystemBlock: false)); }
            catch (EngineeringException ex) { Skip(skipped, path, ProjectTreeNodeTypes.Block, "a block", container, ex); }
        }
        foreach (PlcBlockGroup childGroup in group.Groups)
        {
            try { children.Add(WalkBlockGroup(childGroup, softwareUnitName, path, skipped)); }
            catch (EngineeringException ex) { Skip(skipped, path, ProjectTreeNodeTypes.BlockFolder, "a nested block group", container, ex); }
        }
        if (group is PlcBlockSystemGroup systemGroup)
        {
            foreach (PlcSystemBlockGroup childGroup in systemGroup.SystemBlockGroups)
            {
                try { children.Add(WalkSystemBlockGroup(childGroup, softwareUnitName, path, skipped)); }
                catch (EngineeringException ex) { Skip(skipped, path, ProjectTreeNodeTypes.SystemBlockFolder, "a system block group", container, ex); }
            }
        }
        return new ProjectTreeNode { Name = group.Name, NodeType = ProjectTreeNodeTypes.BlockFolder, Details = null, Children = children };
    }

    private static ProjectTreeNode BuildBlockNode(PlcBlock block, string? softwareUnitName, bool isSystemBlock)
    {
        var details = new Dictionary<string, string>
        {
            ["Number"] = block.Number.ToString(),
            ["ProgrammingLanguage"] = block.ProgrammingLanguage.ToString()
        };
        AddNonBlankDetail(details, "HeaderAuthor", block.HeaderAuthor);
        AddNonBlankDetail(details, "HeaderVersion", block.HeaderVersion?.ToString());
        AddNonBlankDetail(details, "HeaderFamily", block.HeaderFamily);
        AddNonBlankDetail(details, "HeaderName", block.HeaderName);
        if (softwareUnitName is not null) details["SoftwareUnit"] = softwareUnitName;
        if (isSystemBlock) details["IsSystemBlock"] = "true";
        return new ProjectTreeNode
        {
            Name = block.Name,
            NodeType = block switch
            {
                OB => ProjectTreeNodeTypes.Ob,
                FB => ProjectTreeNodeTypes.Fb,
                FC => ProjectTreeNodeTypes.Fc,
                GlobalDB => ProjectTreeNodeTypes.GlobalDb,
                InstanceDB => ProjectTreeNodeTypes.InstanceDb,
                ArrayDB => ProjectTreeNodeTypes.ArrayDb,
                _ => ProjectTreeNodeTypes.Block
            },
            Details = details,
            Children = new List<ProjectTreeNode>()
        };
    }

    private static void AddNonBlankDetail(
        IDictionary<string, string> details,
        string key,
        string? value)
    {
        if (value is not null && !string.IsNullOrWhiteSpace(value))
        {
            details[key] = value;
        }
    }

    private static ProjectTreeNode WalkSystemBlockGroup(
        PlcSystemBlockGroup group,
        string? softwareUnitName,
        IReadOnlyList<ProjectTreeSelectorSegment> parent,
        List<ProjectTreeSkippedNodeInfo> skipped)
    {
        var path = Down(parent, ProjectTreeNodeTypes.SystemBlockFolder, group.Name);
        var container = $"system block group '{group.Name}'";
        var children = new List<ProjectTreeNode>();
        foreach (PlcBlock block in group.Blocks)
        {
            try { children.Add(BuildBlockNode(block, softwareUnitName, isSystemBlock: true)); }
            catch (EngineeringException ex) { Skip(skipped, path, ProjectTreeNodeTypes.Block, "a block", container, ex); }
        }
        foreach (PlcSystemBlockGroup childGroup in group.Groups)
        {
            try { children.Add(WalkSystemBlockGroup(childGroup, softwareUnitName, path, skipped)); }
            catch (EngineeringException ex) { Skip(skipped, path, ProjectTreeNodeTypes.SystemBlockFolder, "a nested system block group", container, ex); }
        }
        return new ProjectTreeNode { Name = group.Name, NodeType = ProjectTreeNodeTypes.SystemBlockFolder, Details = null, Children = children };
    }

    private static ProjectTreeNode WalkTagTableGroup(
        PlcTagTableGroup group,
        IReadOnlyList<ProjectTreeSelectorSegment> parent,
        List<ProjectTreeSkippedNodeInfo> skipped)
    {
        var path = Down(parent, ProjectTreeNodeTypes.TagTableFolder, group.Name);
        var container = $"tag table group '{group.Name}'";
        var children = new List<ProjectTreeNode>();
        foreach (PlcTagTable table in group.TagTables)
        {
            try { children.Add(new ProjectTreeNode { Name = table.Name, NodeType = ProjectTreeNodeTypes.TagTable, Details = null, Children = new List<ProjectTreeNode>() }); }
            catch (EngineeringException ex) { Skip(skipped, path, ProjectTreeNodeTypes.TagTable, "a tag table", container, ex); }
        }
        foreach (PlcTagTableGroup childGroup in group.Groups)
        {
            try { children.Add(WalkTagTableGroup(childGroup, path, skipped)); }
            catch (EngineeringException ex) { Skip(skipped, path, ProjectTreeNodeTypes.TagTableFolder, "a nested tag table group", container, ex); }
        }
        return new ProjectTreeNode { Name = group.Name, NodeType = ProjectTreeNodeTypes.TagTableFolder, Details = null, Children = children };
    }

    private static ProjectTreeNode WalkTypeGroup(
        PlcTypeGroup group,
        IReadOnlyList<ProjectTreeSelectorSegment> parent,
        List<ProjectTreeSkippedNodeInfo> skipped)
    {
        var path = Down(parent, ProjectTreeNodeTypes.TypeFolder, group.Name);
        var container = $"type group '{group.Name}'";
        var children = new List<ProjectTreeNode>();
        foreach (PlcType type in group.Types)
        {
            try { children.Add(new ProjectTreeNode { Name = type.Name, NodeType = ProjectTreeNodeTypes.Type, Details = null, Children = new List<ProjectTreeNode>() }); }
            catch (EngineeringException ex) { Skip(skipped, path, ProjectTreeNodeTypes.Type, "a PLC data type", container, ex); }
        }
        foreach (PlcTypeGroup childGroup in group.Groups)
        {
            try { children.Add(WalkTypeGroup(childGroup, path, skipped)); }
            catch (EngineeringException ex) { Skip(skipped, path, ProjectTreeNodeTypes.TypeFolder, "a nested PLC data type group", container, ex); }
        }
        return new ProjectTreeNode { Name = group.Name, NodeType = ProjectTreeNodeTypes.TypeFolder, Details = null, Children = children };
    }
}
