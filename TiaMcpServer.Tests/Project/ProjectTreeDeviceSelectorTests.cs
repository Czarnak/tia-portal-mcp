using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Project;

public sealed class ProjectTreeDeviceSelectorTests
{
    // Production bug caught: preselection could reject the correct device solely because name casing differs.
    [Fact]
    public void MatchingName_IsCaseInsensitiveAndReturnsTheObservedDevice()
    {
        var expected = new TestDevice("PLC_1", 17);
        var devices = new[] { new TestDevice("PLC_0", 9), expected };

        var selected = ProjectTreeDeviceSelector.Select(
            devices,
            device => device.Name,
            DeviceSegment("plc_1"));

        Assert.Same(expected, selected);
    }

    // Production bug caught: preselection could normalize whitespace and select a name the caller did not supply exactly.
    [Fact]
    public void MatchingName_DoesNotTrimTheRequestedName()
    {
        var error = Assert.Throws<ProjectTreeSelectionException>(() =>
            ProjectTreeDeviceSelector.Select(
                new[] { new TestDevice("PLC_1", 17) },
                device => device.Name,
                DeviceSegment(" PLC_1 ")));

        Assert.Equal(WorkerFailureCategories.TargetNotFound, error.Category);
    }

    // Production bug caught: a missing device must preserve the stable target-not-found category.
    [Fact]
    public void MissingDevice_IsTargetNotFound()
    {
        var error = Assert.Throws<ProjectTreeSelectionException>(() =>
            ProjectTreeDeviceSelector.Select(
                new[] { new TestDevice("PLC_1", 17) },
                device => device.Name,
                DeviceSegment("PLC_9")));

        Assert.Equal(WorkerFailureCategories.TargetNotFound, error.Category);
    }

    // Production bug caught: duplicate case-insensitive device names must not select the first enumerated device.
    [Fact]
    public void DuplicateDeviceName_IsTargetAmbiguous()
    {
        var error = Assert.Throws<ProjectTreeSelectionException>(() =>
            ProjectTreeDeviceSelector.Select(
                new[] { new TestDevice("PLC_1", 17), new TestDevice("plc_1", 23) },
                device => device.Name,
                DeviceSegment("PLC_1")));

        Assert.Equal(WorkerFailureCategories.TargetAmbiguous, error.Category);
    }

    // Production bug caught: device preselection could accept a segment for a different node type.
    [Fact]
    public void NonDeviceSegment_IsInvalidSelector()
    {
        var error = Assert.Throws<ProjectTreeSelectionException>(() =>
            ProjectTreeDeviceSelector.Select(
                new[] { new TestDevice("PLC_1", 17) },
                device => device.Name,
                new ProjectTreeSelectorSegment
                {
                    NodeType = ProjectTreeNodeTypes.PlcSoftware,
                    Name = "PLC_1"
                }));

        Assert.Equal(WorkerFailureCategories.InvalidSelector, error.Category);
    }

    private static ProjectTreeSelectorSegment DeviceSegment(string name)
        => new() { NodeType = ProjectTreeNodeTypes.Device, Name = name };

    private sealed record TestDevice(string Name, int Marker);
}
