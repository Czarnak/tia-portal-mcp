using System;
using System.IO;
using System.Linq;
using Xunit;

namespace TiaMcpServer.Tests.Project;

public class ProjectTraversalSourceContractTests
{
    [Fact]
    public void ProjectDeviceEnumerator_TraversesDirectAndNestedGroupsInOrder()
    {
        var source = ReadRepositorySource(
            "TiaMcpServer.OpennessWorker", "Openness", "ProjectDeviceEnumerator.cs");

        Assert.Contains("EnumerateWithLocations(project)", source, StringComparison.Ordinal);
        Assert.Contains("foreach (LocatedProjectDevice locatedDevice in EnumerateWithLocations(project))", source, StringComparison.Ordinal);
        Assert.Contains("foreach (Device device in project.Devices)", source, StringComparison.Ordinal);
        Assert.Contains("foreach (DeviceUserGroup group in project.DeviceGroups)", source, StringComparison.Ordinal);
        Assert.Contains("foreach (Device device in group.Devices)", source, StringComparison.Ordinal);
        Assert.Contains("foreach (DeviceUserGroup childGroup in group.Groups)", source, StringComparison.Ordinal);
        Assert.Contains("devices/{deviceIndex}", source, StringComparison.Ordinal);
        Assert.Contains("deviceGroups/{groupIndex}", source, StringComparison.Ordinal);
        Assert.Contains("groups/{childGroupIndex}", source, StringComparison.Ordinal);

        Assert.True(
            source.IndexOf("foreach (Device device in project.Devices)", StringComparison.Ordinal) <
            source.IndexOf("foreach (DeviceUserGroup group in project.DeviceGroups)", StringComparison.Ordinal),
            "Direct project devices must be enumerated before project device groups.");
        Assert.True(
            source.IndexOf("foreach (Device device in group.Devices)", StringComparison.Ordinal) <
            source.IndexOf("foreach (DeviceUserGroup childGroup in group.Groups)", StringComparison.Ordinal),
            "Direct group devices must be enumerated before child device groups.");
    }

    [Fact]
    public void ProjectDeviceEnumerator_KeepsStructuralLocatorsInternalToTheWorkerTraversal()
    {
        var source = ReadRepositorySource(
            "TiaMcpServer.OpennessWorker", "Openness", "ProjectDeviceEnumerator.cs");

        Assert.Contains("internal sealed class LocatedProjectDevice", source, StringComparison.Ordinal);
        Assert.Contains("StructuralLocator", source, StringComparison.Ordinal);
        Assert.DoesNotContain("HardwareConfigInfo", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SubnetInfo", source, StringComparison.Ordinal);
    }

    [Fact]
    public void HardwareAndTreeReaders_UseTheSameProjectDeviceEnumerator()
    {
        var hardware = ReadRepositorySource(
            "TiaMcpServer.OpennessWorker", "Openness", "HardwareConfigReader.cs");
        var tree = ReadRepositorySource(
            "TiaMcpServer.OpennessWorker", "Openness", "ProjectTreeWalker.cs");

        Assert.Contains("foreach (Device device in ProjectDeviceEnumerator.Enumerate(project))", hardware, StringComparison.Ordinal);
        Assert.Contains("foreach (Device device in ProjectDeviceEnumerator.Enumerate(project))", tree, StringComparison.Ordinal);
        Assert.DoesNotContain("foreach (Device device in project.Devices)", hardware, StringComparison.Ordinal);
        Assert.DoesNotContain("foreach (Device device in project.Devices)", tree, StringComparison.Ordinal);
        Assert.Contains("rootNodes.Add(WalkDevice(device));", tree, StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectTreeWalker_TraversesEverySystemBlockGroupWithItsOwnTypedWalker()
    {
        var source = ReadRepositorySource(
            "TiaMcpServer.OpennessWorker", "Openness", "ProjectTreeWalker.cs");

        Assert.Contains("group is PlcBlockSystemGroup systemGroup", source, StringComparison.Ordinal);
        Assert.Contains("foreach (PlcSystemBlockGroup childGroup in systemGroup.SystemBlockGroups)", source, StringComparison.Ordinal);
        Assert.Contains("WalkSystemBlockGroup(childGroup", source, StringComparison.Ordinal);
        Assert.Contains("foreach (PlcBlock block in group.Blocks)", source, StringComparison.Ordinal);
        Assert.Contains("foreach (PlcSystemBlockGroup childGroup in group.Groups)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectTreeWalker_MarksSystemMembershipWithoutChangingFunctionalBlockTypes()
    {
        var source = ReadRepositorySource(
            "TiaMcpServer.OpennessWorker", "Openness", "ProjectTreeWalker.cs");

        Assert.Contains("NodeType = \"SystemBlockFolder\"", source, StringComparison.Ordinal);
        Assert.Contains("details[\"IsSystemBlock\"] = \"true\";", source, StringComparison.Ordinal);
        Assert.Contains("BuildBlockNode(block, path, softwareUnitName, isSystemBlock: false)", source, StringComparison.Ordinal);
        Assert.Contains("BuildBlockNode(block, path, softwareUnitName, isSystemBlock: true)", source, StringComparison.Ordinal);
        Assert.Equal(1, source.Split("NodeType = block switch", StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("HeaderAuthor", source, StringComparison.Ordinal);
    }

    private static string ReadRepositorySource(params string[] pathSegments)
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            var candidate = Path.Combine(new[] { current }.Concat(pathSegments).ToArray());
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate).Replace("\r\n", "\n");
            }

            current = Path.GetDirectoryName(current);
        }

        throw new FileNotFoundException(
            $"Could not find repository file '{Path.Combine(pathSegments)}'.");
    }
}
