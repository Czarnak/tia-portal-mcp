using Xunit;

namespace TiaMcpServer.Tests.Project;

public class ProjectTraversalSourceContractTests
{
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
            "TiaMcpServer.OpennessWorker", "Openness", "ProjectTreeSnapshotWalker.cs");

        Assert.Contains("capture.Traverse(() => ProjectDeviceEnumerator.Enumerate(project), device =>", hardware, StringComparison.Ordinal);
        Assert.Contains("candidates.Add((device, nameEvidence));", hardware, StringComparison.Ordinal);
        Assert.Contains("}, \"deviceEnumeration\", \"deviceMaterialization\");", hardware, StringComparison.Ordinal);
        Assert.Contains("result.DiscoveryEvidence = capture.Evidence;", hardware, StringComparison.Ordinal);
        Assert.Contains("ProjectDeviceEnumerator.Enumerate(project).Cast<Device>().ToList()", tree, StringComparison.Ordinal);
        Assert.DoesNotContain("foreach (Device device in project.Devices)", hardware, StringComparison.Ordinal);
        Assert.DoesNotContain("foreach (Device device in project.Devices)", tree, StringComparison.Ordinal);
        Assert.Contains("devices.Select(device => WalkDevice(device, skipped)).ToList()", tree, StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectTreeSnapshotWalker_TraversesEverySystemBlockGroupWithItsOwnTypedWalker()
    {
        var source = ReadRepositorySource(
            "TiaMcpServer.OpennessWorker", "Openness", "ProjectTreeSnapshotWalker.cs");

        Assert.Contains("group is PlcBlockSystemGroup systemGroup", source, StringComparison.Ordinal);
        Assert.Contains("foreach (PlcSystemBlockGroup childGroup in systemGroup.SystemBlockGroups)", source, StringComparison.Ordinal);
        Assert.Contains("WalkSystemBlockGroup(childGroup", source, StringComparison.Ordinal);
        Assert.Contains("foreach (PlcBlock block in group.Blocks)", source, StringComparison.Ordinal);
        Assert.Contains("foreach (PlcSystemBlockGroup childGroup in group.Groups)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectTreeSnapshotWalker_MarksSystemMembershipWithoutChangingFunctionalBlockTypes()
    {
        var source = ReadRepositorySource(
            "TiaMcpServer.OpennessWorker", "Openness", "ProjectTreeSnapshotWalker.cs");

        Assert.Contains("NodeType = ProjectTreeNodeTypes.SystemBlockFolder", source, StringComparison.Ordinal);
        Assert.Contains("details[\"IsSystemBlock\"] = \"true\";", source, StringComparison.Ordinal);
        Assert.Contains("BuildBlockNode(block, softwareUnitName, isSystemBlock: false)", source, StringComparison.Ordinal);
        Assert.Contains("BuildBlockNode(block, softwareUnitName, isSystemBlock: true)", source, StringComparison.Ordinal);
        Assert.Equal(1, source.Split("NodeType = block switch", StringSplitOptions.None).Length - 1);
        Assert.Contains("block.HeaderAuthor", source, StringComparison.Ordinal);
        Assert.Contains("block.HeaderVersion", source, StringComparison.Ordinal);
        Assert.Contains("block.HeaderFamily", source, StringComparison.Ordinal);
        Assert.Contains("block.HeaderName", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetAttribute(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SelectedDevice_IsResolvedBeforeAnyPlcSoftwareDiscovery()
    {
        var source = ReadRepositorySource(
            "TiaMcpServer.OpennessWorker", "Openness", "ProjectTreeSnapshotWalker.cs");
        var enumerate = source.IndexOf("ProjectDeviceEnumerator.Enumerate(project)", StringComparison.Ordinal);
        var select = source.IndexOf("ProjectTreeDeviceSelector.Select", StringComparison.Ordinal);
        var walk = source.IndexOf("WalkDevice(selectedDevice, skipped)", StringComparison.Ordinal);

        Assert.True(enumerate >= 0 && select > enumerate && walk > select);
        Assert.DoesNotContain("[\"Path\"]", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CombinePath", source, StringComparison.Ordinal);
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
