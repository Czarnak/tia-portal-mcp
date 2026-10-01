using Siemens.Engineering.HW;
using TiaMcpServer.OpennessWorker.Openness;
using Xunit;
using SiemensProject = Siemens.Engineering.Project;

namespace TiaMcpServer.Tests.Project;

public sealed class ProjectDeviceEnumeratorTests
{
    [Fact]
    public void EnumerateWithLocations_IncludesAllScopesInStructuralOrder()
    {
        var project = new SiemensProject();
        project.Devices.Items.Add(new Device { Name = "CPU" });
        project.Devices.Items.Add(new Device { Name = "HMI" });
        var group = new DeviceUserGroup { Name = "Area" };
        group.Devices.Items.Add(new Device { Name = "Grouped" });
        var nested = new DeviceUserGroup { Name = "Nested" };
        nested.Devices.Items.Add(new Device { Name = "Nested device" });
        group.Groups.Items.Add(nested);
        project.DeviceGroups.Items.Add(group);
        project.UngroupedDevicesGroup.Devices.Items.Add(new Device { Name = "ET 200SP" });
        project.UngroupedDevicesGroup.Devices.Items.Add(new Device { Name = "Switch" });

        var devices = ProjectDeviceEnumerator.EnumerateWithLocations(project);

        Assert.Equal(new[] { "CPU", "HMI", "Grouped", "Nested device", "ET 200SP", "Switch" },
            devices.Select(device => device.Device.Name));
        Assert.Equal(new[]
        {
            "devices/0", "devices/1", "deviceGroups/0/devices/0",
            "deviceGroups/0/groups/0/devices/0", "ungroupedDevices/0", "ungroupedDevices/1"
        }, devices.Select(device => device.StructuralLocator));
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5 }, devices.Select(device => device.SourceOrder));
    }

    [Fact]
    public void Enumerate_EmptyUngroupedCollectionIsValid()
    {
        var project = new SiemensProject();
        var direct = new Device { Name = "CPU" };
        project.Devices.Items.Add(direct);

        Assert.Same(direct, Assert.Single(ProjectDeviceEnumerator.Enumerate(project)));
    }

    [Theory]
    [InlineData("group")]
    [InlineData("devices")]
    [InlineData("enumeration")]
    public void Enumerate_PropagatesUngroupedFailureBeforeReturningAnyDevice(string failureAt)
    {
        var project = new SiemensProject();
        project.Devices.Items.Add(new Device { Name = "CPU" });
        project.UngroupedDevicesGroup.Devices.Items.Add(new Device { Name = "ET 200SP" });
        var failure = new IOException("Incomplete device inventory.");
        switch (failureAt)
        {
            case "group": project.UngroupedDevicesGroupFailure = failure; break;
            case "devices": project.UngroupedDevicesGroup.DevicesFailure = failure; break;
            default: project.UngroupedDevicesGroup.Devices.EnumerationFailure = failure; break;
        }

        using var devices = ProjectDeviceEnumerator.Enumerate(project).GetEnumerator();
        Assert.Same(failure, Assert.Throws<IOException>(() => devices.MoveNext()));
    }

    [Theory]
    [InlineData("group")]
    [InlineData("devices")]
    public void Enumerate_RejectsNullUngroupedApiMembers(string nullMember)
    {
        var project = new SiemensProject();
        project.Devices.Items.Add(new Device { Name = "CPU" });
        if (nullMember == "group")
            project.UngroupedDevicesGroup = null!;
        else
            project.UngroupedDevicesGroup.Devices = null!;

        Assert.Throws<InvalidOperationException>(() => ProjectDeviceEnumerator.Enumerate(project).ToList());
    }

    [Fact]
    public void Enumerate_RejectsInvalidUngroupedEntry()
    {
        var project = new SiemensProject();
        project.UngroupedDevicesGroup.Devices.Items.Add(null!);

        Assert.Throws<InvalidOperationException>(() => ProjectDeviceEnumerator.Enumerate(project).ToList());
    }
}
