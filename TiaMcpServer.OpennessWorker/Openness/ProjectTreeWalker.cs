using System;
using System.Collections.Generic;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Tags;
using Siemens.Engineering.SW.Types;
using Siemens.Engineering.SW.Units;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

public class ProjectTreeWalker
{
    public List<ProjectTreeNode> Walk(Project project)
    {
        var rootNodes = new List<ProjectTreeNode>();

        foreach (Device device in project.Devices)
        {
            var children = new List<ProjectTreeNode>();

            foreach (var plcSoftware in PlcSoftwareLocator.FindInDevice(device))
            {
                children.Add(WalkPlcSoftware(device.Name, plcSoftware));
            }

            rootNodes.Add(new ProjectTreeNode
            {
                Name = device.Name,
                NodeType = "Device",
                Details = new Dictionary<string, string>
                {
                    ["Path"] = device.Name
                },
                Children = children
            });
        }

        return rootNodes;
    }

    private static ProjectTreeNode WalkPlcSoftware(string deviceName, PlcSoftware plcSoftware)
    {
        var softwareName = plcSoftware.Name;
        var children = new List<ProjectTreeNode>
        {
            WalkBlockGroup(plcSoftware.BlockGroup, CombinePath(softwareName, "Blocks"), softwareUnitName: null),
            WalkTagTableGroup(plcSoftware.TagTableGroup, CombinePath(softwareName, "TagTables")),
            WalkTypeGroup(plcSoftware.TypeGroup, CombinePath(softwareName, "Types"))
        };

        children.AddRange(WalkSoftwareUnits(softwareName, plcSoftware));

        return new ProjectTreeNode
        {
            Name = plcSoftware.Name,
            NodeType = "PlcSoftware",
            Details = new Dictionary<string, string>
            {
                ["Path"] = deviceName
            },
            Children = children
        };
    }

    private static IEnumerable<ProjectTreeNode> WalkSoftwareUnits(string softwareName, PlcSoftware plcSoftware)
    {
        PlcUnitProvider? unitProvider = null;

        try
        {
            unitProvider = plcSoftware.GetService<PlcUnitProvider>();
        }
        catch (EngineeringException ex)
        {
            Console.Error.WriteLine($"Skipping software units for PLC software '{plcSoftware.Name}': {ex.Message}");
        }

        if (unitProvider is null)
        {
            yield break;
        }

        foreach (PlcUnit unit in unitProvider.UnitGroup.Units)
        {
            var unitPath = CombinePath(softwareName, "Units", unit.Name);

            yield return new ProjectTreeNode
            {
                Name = unit.Name,
                NodeType = "SoftwareUnit",
                Details = new Dictionary<string, string>
                {
                    ["Path"] = unitPath
                },
                Children = new List<ProjectTreeNode>
                {
                    WalkBlockGroup(unit.BlockGroup, CombinePath(unitPath, "Blocks"), unit.Name),
                    WalkTagTableGroup(unit.TagTableGroup, CombinePath(unitPath, "TagTables")),
                    WalkTypeGroup(unit.TypeGroup, CombinePath(unitPath, "Types"))
                }
            };
        }
    }

    private static ProjectTreeNode WalkBlockGroup(PlcBlockGroup group, string path, string? softwareUnitName)
    {
        var children = new List<ProjectTreeNode>();

        foreach (PlcBlock block in group.Blocks)
        {
            try
            {
                var details = new Dictionary<string, string>
                {
                    ["Path"] = CombinePath(path, block.Name),
                    ["Number"] = block.Number.ToString(),
                    ["ProgrammingLanguage"] = block.ProgrammingLanguage.ToString()
                };

                if (softwareUnitName is not null)
                {
                    details["SoftwareUnit"] = softwareUnitName;
                }

                children.Add(new ProjectTreeNode
                {
                    Name = block.Name,
                    NodeType = block switch
                    {
                        OB => "OB",
                        FB => "FB",
                        FC => "FC",
                        GlobalDB => "GlobalDB",
                        InstanceDB => "InstanceDB",
                        ArrayDB => "ArrayDB",
                        _ => "Block"
                    },
                    Details = details
                });
            }
            catch (EngineeringException ex)
            {
                Console.Error.WriteLine($"Skipping a block while walking block group '{group.Name}': {ex.Message}");
            }
        }

        foreach (PlcBlockGroup childGroup in group.Groups)
        {
            try
            {
                children.Add(WalkBlockGroup(childGroup, CombinePath(path, childGroup.Name), softwareUnitName));
            }
            catch (EngineeringException ex)
            {
                Console.Error.WriteLine($"Skipping a nested block group while walking block group '{group.Name}': {ex.Message}");
            }
        }

        return new ProjectTreeNode
        {
            Name = group.Name,
            NodeType = "BlockFolder",
            Details = new Dictionary<string, string>
            {
                ["Path"] = path
            },
            Children = children
        };
    }

    private static ProjectTreeNode WalkTagTableGroup(PlcTagTableGroup group, string path)
    {
        var children = new List<ProjectTreeNode>();

        foreach (PlcTagTable table in group.TagTables)
        {
            try
            {
                children.Add(new ProjectTreeNode
                {
                    Name = table.Name,
                    NodeType = "TagTable",
                    Details = new Dictionary<string, string>
                    {
                        ["Path"] = CombinePath(path, table.Name)
                    }
                });
            }
            catch (EngineeringException ex)
            {
                Console.Error.WriteLine($"Skipping a tag table while walking tag table group '{group.Name}': {ex.Message}");
            }
        }

        foreach (PlcTagTableGroup childGroup in group.Groups)
        {
            try
            {
                children.Add(WalkTagTableGroup(childGroup, CombinePath(path, childGroup.Name)));
            }
            catch (EngineeringException ex)
            {
                Console.Error.WriteLine($"Skipping a nested tag table group while walking tag table group '{group.Name}': {ex.Message}");
            }
        }

        return new ProjectTreeNode
        {
            Name = group.Name,
            NodeType = "TagTableFolder",
            Details = new Dictionary<string, string>
            {
                ["Path"] = path
            },
            Children = children
        };
    }

    private static ProjectTreeNode WalkTypeGroup(PlcTypeGroup group, string path)
    {
        var children = new List<ProjectTreeNode>();

        foreach (PlcType type in group.Types)
        {
            try
            {
                children.Add(new ProjectTreeNode
                {
                    Name = type.Name,
                    NodeType = "Type",
                    Details = new Dictionary<string, string>
                    {
                        ["Path"] = CombinePath(path, type.Name)
                    }
                });
            }
            catch (EngineeringException ex)
            {
                Console.Error.WriteLine($"Skipping a type while walking type group '{group.Name}': {ex.Message}");
            }
        }

        foreach (PlcTypeGroup childGroup in group.Groups)
        {
            try
            {
                children.Add(WalkTypeGroup(childGroup, CombinePath(path, childGroup.Name)));
            }
            catch (EngineeringException ex)
            {
                Console.Error.WriteLine($"Skipping a nested type group while walking type group '{group.Name}': {ex.Message}");
            }
        }

        return new ProjectTreeNode
        {
            Name = group.Name,
            NodeType = "TypeFolder",
            Details = new Dictionary<string, string>
            {
                ["Path"] = path
            },
            Children = children
        };
    }

    private static string CombinePath(params string[] segments)
    {
        return string.Join("/", segments);
    }
}
