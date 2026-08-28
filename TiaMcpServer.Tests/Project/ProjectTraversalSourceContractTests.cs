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

        Assert.Contains("foreach (Device device in project.Devices)", source, StringComparison.Ordinal);
        Assert.Contains("foreach (DeviceUserGroup group in project.DeviceGroups)", source, StringComparison.Ordinal);
        Assert.Contains("foreach (Device device in group.Devices)", source, StringComparison.Ordinal);
        Assert.Contains("foreach (DeviceUserGroup childGroup in group.Groups)", source, StringComparison.Ordinal);
        Assert.Contains("foreach (var device in Enumerate(childGroup))", source, StringComparison.Ordinal);
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
