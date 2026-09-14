using System;
using System.Collections.Generic;
using System.Linq;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Tags;
using Siemens.Engineering.SW.Types;
using Siemens.Engineering.SW.Units;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

/// <summary>Materializes a typed tree snapshot without legacy presentation paths.</summary>
public sealed class ProjectTreeSnapshotWalker
{
    public ProjectTreeSelectionResult WalkSnapshot(Project project, IReadOnlyList<ProjectTreeSelectorSegment>? startSelector, int? depth)
    {
        ProjectTreeNodeTypes.Validate(startSelector);
        var devices = ProjectDeviceEnumerator.Enumerate(project).Cast<Device>().ToList();

        if (startSelector is null)
        {
            var roots = devices.Select(WalkDevice).ToList();
            return ProjectTreeFilter.Apply(roots, startSelector: null, depth);
        }

        var selectedDevice = ProjectTreeDeviceSelector.Select(devices, device => device.Name, startSelector[0]);
        var selectedRoot = WalkDevice(selectedDevice);
        return ProjectTreeFilter.Apply(new List<ProjectTreeNode> { selectedRoot }, startSelector, depth);
    }

    private static ProjectTreeNode WalkDevice(Device device)
    {
        var children = new List<ProjectTreeNode>();
        foreach (var plcSoftware in PlcSoftwareLocator.FindInDevice(device))
        {
            children.Add(WalkPlcSoftware(plcSoftware));
        }
        return new ProjectTreeNode { Name = device.Name, NodeType = ProjectTreeNodeTypes.Device, Details = null, Children = children };
    }

    private static ProjectTreeNode WalkPlcSoftware(PlcSoftware plcSoftware)
    {
        var children = new List<ProjectTreeNode>
        {
            WalkBlockGroup(plcSoftware.BlockGroup, softwareUnitName: null),
            WalkTagTableGroup(plcSoftware.TagTableGroup),
            WalkTypeGroup(plcSoftware.TypeGroup)
        };
        children.AddRange(WalkSoftwareUnits(plcSoftware));
        return new ProjectTreeNode { Name = plcSoftware.Name, NodeType = ProjectTreeNodeTypes.PlcSoftware, Details = null, Children = children };
    }

    private static IEnumerable<ProjectTreeNode> WalkSoftwareUnits(PlcSoftware plcSoftware)
    {
        PlcUnitProvider? unitProvider = null;
        try { unitProvider = plcSoftware.GetService<PlcUnitProvider>(); }
        catch (EngineeringException ex) { Console.Error.WriteLine($"Skipping software units for PLC software '{plcSoftware.Name}': {ex.Message}"); }
        if (unitProvider is null) yield break;

        foreach (PlcUnit unit in unitProvider.UnitGroup.Units)
        {
            yield return new ProjectTreeNode
            {
                Name = unit.Name,
                NodeType = ProjectTreeNodeTypes.SoftwareUnit,
                Details = null,
                Children = new List<ProjectTreeNode>
                {
                    WalkBlockGroup(unit.BlockGroup, unit.Name),
                    WalkTagTableGroup(unit.TagTableGroup),
                    WalkTypeGroup(unit.TypeGroup)
                }
            };
        }
    }

    private static ProjectTreeNode WalkBlockGroup(PlcBlockGroup group, string? softwareUnitName)
    {
        var children = new List<ProjectTreeNode>();
        foreach (PlcBlock block in group.Blocks)
        {
            try { children.Add(BuildBlockNode(block, softwareUnitName, isSystemBlock: false)); }
            catch (EngineeringException ex) { Console.Error.WriteLine($"Skipping a block while walking block group '{group.Name}': {ex.Message}"); }
        }
        foreach (PlcBlockGroup childGroup in group.Groups)
        {
            try { children.Add(WalkBlockGroup(childGroup, softwareUnitName)); }
            catch (EngineeringException ex) { Console.Error.WriteLine($"Skipping a nested block group while walking block group '{group.Name}': {ex.Message}"); }
        }
        if (group is PlcBlockSystemGroup systemGroup)
        {
            foreach (PlcSystemBlockGroup childGroup in systemGroup.SystemBlockGroups)
            {
                try { children.Add(WalkSystemBlockGroup(childGroup, softwareUnitName)); }
                catch (EngineeringException ex) { Console.Error.WriteLine($"Skipping a system block group while walking block group '{group.Name}': {ex.Message}"); }
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

    private static ProjectTreeNode WalkSystemBlockGroup(PlcSystemBlockGroup group, string? softwareUnitName)
    {
        var children = new List<ProjectTreeNode>();
        foreach (PlcBlock block in group.Blocks)
        {
            try { children.Add(BuildBlockNode(block, softwareUnitName, isSystemBlock: true)); }
            catch (EngineeringException ex) { Console.Error.WriteLine($"Skipping a block while walking system block group '{group.Name}': {ex.Message}"); }
        }
        foreach (PlcSystemBlockGroup childGroup in group.Groups)
        {
            try { children.Add(WalkSystemBlockGroup(childGroup, softwareUnitName)); }
            catch (EngineeringException ex) { Console.Error.WriteLine($"Skipping a nested system block group while walking system block group '{group.Name}': {ex.Message}"); }
        }
        return new ProjectTreeNode { Name = group.Name, NodeType = ProjectTreeNodeTypes.SystemBlockFolder, Details = null, Children = children };
    }

    private static ProjectTreeNode WalkTagTableGroup(PlcTagTableGroup group)
    {
        var children = new List<ProjectTreeNode>();
        foreach (PlcTagTable table in group.TagTables)
        {
            try { children.Add(new ProjectTreeNode { Name = table.Name, NodeType = ProjectTreeNodeTypes.TagTable, Details = null, Children = new List<ProjectTreeNode>() }); }
            catch (EngineeringException ex) { Console.Error.WriteLine($"Skipping a tag table while walking tag table group '{group.Name}': {ex.Message}"); }
        }
        foreach (PlcTagTableGroup childGroup in group.Groups)
        {
            try { children.Add(WalkTagTableGroup(childGroup)); }
            catch (EngineeringException ex) { Console.Error.WriteLine($"Skipping a nested tag table group while walking tag table group '{group.Name}': {ex.Message}"); }
        }
        return new ProjectTreeNode { Name = group.Name, NodeType = ProjectTreeNodeTypes.TagTableFolder, Details = null, Children = children };
    }

    private static ProjectTreeNode WalkTypeGroup(PlcTypeGroup group)
    {
        var children = new List<ProjectTreeNode>();
        foreach (PlcType type in group.Types)
        {
            try { children.Add(new ProjectTreeNode { Name = type.Name, NodeType = ProjectTreeNodeTypes.Type, Details = null, Children = new List<ProjectTreeNode>() }); }
            catch (EngineeringException ex) { Console.Error.WriteLine($"Skipping a PLC data type while walking type group '{group.Name}': {ex.Message}"); }
        }
        foreach (PlcTypeGroup childGroup in group.Groups)
        {
            try { children.Add(WalkTypeGroup(childGroup)); }
            catch (EngineeringException ex) { Console.Error.WriteLine($"Skipping a nested PLC data type group while walking type group '{group.Name}': {ex.Message}"); }
        }
        return new ProjectTreeNode { Name = group.Name, NodeType = ProjectTreeNodeTypes.TypeFolder, Details = null, Children = children };
    }
}
